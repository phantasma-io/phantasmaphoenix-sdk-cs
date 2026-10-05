using PhantasmaPhoenix.Cryptography;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

/// <summary>
/// Signs a <see cref="TxMsg"/> into the envelope the chain reads (<see cref="SignedTxMsg"/>).
/// <para>
/// The message must already carry its gas offer. A zero <c>maxGas</c> is never admitted, so it marks
/// a message that was built and not planned. Signing such a message would only produce a rejection.
/// A native fungible transfer of zero atoms, or of 2^63 atoms or more, is refused too: the chain
/// admits it, the transfer fails, and the failed transaction is billed.
/// </para>
/// </summary>
public static class TxMsgSigner
{
	/// <summary>Signs a single-witness message with in-memory keys and returns the serialized
	/// envelope.</summary>
	public static byte[] Sign(TxMsg msg, PhantasmaKeys keys) => SignAndSerializeWithKeys(msg, keys);

	/// <summary>
	/// Signs a message with in-memory keys, one per witness the message needs. It works for every
	/// transaction type. A gas-payer transfer takes the gas payer's key and the owner's key in any
	/// order. A Call takes the keys of every witness the contract will check.
	/// </summary>
	public static SignedTxMsg SignWithKeys(TxMsg msg, params IKeyPair[] keys)
	{
		AssertSignable(msg);
		if (keys == null)
			throw new ArgumentNullException(nameof(keys));
		var message = CarbonBlob.Serialize(msg);
		var signers = new ITxSigner[keys.Length];
		for (var i = 0; i < keys.Length; i++)
			signers[i] = new KeyPairSigner(keys[i]);
		var slots = WitnessSlots(msg, signers);
		var witnesses = new Witness[slots.Length];
		for (var i = 0; i < slots.Length; i++)
		{
			witnesses[i] = new Witness
			{
				address = slots[i].Address,
				signature = new Bytes64(((KeyPairSigner)slots[i].Signer).Sign(message))
			};
		}
		return new SignedTxMsg { msg = msg, witnesses = witnesses };
	}

	public static byte[] SignAndSerializeWithKeys(TxMsg msg, params IKeyPair[] keys) =>
		CarbonBlob.Serialize(SignWithKeys(msg, keys));

	/// <summary>
	/// Signs a message with any <see cref="ITxSigner"/>, one per witness. A signer can hold keys, drive
	/// a hardware wallet or call a remote service. Every signer signs the same serialized message.
	/// <para>
	/// One account can fill two witness slots, when it pays the gas and owns the tokens. Such a signer
	/// is asked once and its signature is reused.
	/// </para>
	/// </summary>
	public static async Task<SignedTxMsg> SignWithAsync(TxMsg msg, IReadOnlyList<ITxSigner> signers, CancellationToken cancellationToken = default)
	{
		AssertSignable(msg);
		if (signers == null)
			throw new ArgumentNullException(nameof(signers));
		var message = CarbonBlob.Serialize(msg);
		var slots = WitnessSlots(msg, signers);
		var signatures = new Dictionary<Bytes32, Bytes64>();
		var witnesses = new Witness[slots.Length];
		for (var i = 0; i < slots.Length; i++)
		{
			if (!signatures.TryGetValue(slots[i].Address, out var signature))
			{
				var raw = await slots[i].Signer.SignAsync(message, cancellationToken).ConfigureAwait(false);
				if (raw == null || raw.Length != 64)
					throw new InvalidOperationException($"Signer {slots[i].Address.ToHex()} returned {(raw == null ? "no" : raw.Length + " bytes of")} signature; an Ed25519 signature is 64 bytes");
				signature = new Bytes64(raw);
				signatures[slots[i].Address] = signature;
			}
			witnesses[i] = new Witness { address = slots[i].Address, signature = signature };
		}
		return new SignedTxMsg { msg = msg, witnesses = witnesses };
	}

	public static async Task<byte[]> SignAndSerializeWithAsync(TxMsg msg, IReadOnlyList<ITxSigner> signers, CancellationToken cancellationToken = default) =>
		CarbonBlob.Serialize(await SignWithAsync(msg, signers, cancellationToken).ConfigureAwait(false));

	// Refuses, before anything is signed, a message the chain is certain to refuse.
	private static void AssertSignable(in TxMsg msg)
	{
		// A zero gas offer is never admissible, so it marks a message that was built and not planned.
		// Plan it with PhantasmaAPI.Fees or FeePlanner, or set maxGas on purpose.
		if (msg.maxGas == 0)
			throw new InvalidOperationException("Transaction has no gas offer: plan its fees or set maxGas before signing");

		// A native fungible transfer carries its amount as a u64, and the chain reads it as a signed
		// 64-bit value that must be above zero. Zero, and an amount of 2^63 or more, fail on chain for
		// every fungible token, big-fungible ones included, and the failed transaction is billed. A
		// larger amount needs a Token.TransferFungible module call or a script transfer.
		ulong? amount = null;
		if (msg.type == TxTypes.TransferFungible && msg.msg is TxMsgTransferFungible plain)
			amount = plain.amount;
		else if (msg.type == TxTypes.TransferFungible_GasPayer && msg.msg is TxMsgTransferFungible_GasPayer withGasPayer)
			amount = withGasPayer.amount;
		if (amount == 0)
			throw new InvalidOperationException("Transfer amount must be above zero for a native transfer");
		if (amount > long.MaxValue)
			throw new InvalidOperationException($"Transfer amount {amount} is above the int64 maximum the chain accepts in a native transfer");
	}

	private readonly struct WitnessSlot
	{
		public readonly Bytes32 Address;
		public readonly ITxSigner Signer;

		public WitnessSlot(Bytes32 address, ITxSigner signer)
		{
			Address = address;
			Signer = signer;
		}
	}

	// Pairs every witness slot of the envelope with the signer that owns its address.
	//
	// The node fixes the witness set of the native transfers, mints and burns, and of their gas-payer
	// variants. For those the slots come in envelope order however the signers were passed, and the
	// signer set must match the required addresses exactly. A missing owner key or a stray extra key
	// is a caller mistake, and the node would reject it later at a cost.
	//
	// For the witness-array types the caller's order is the envelope order.
	private static WitnessSlot[] WitnessSlots(in TxMsg msg, IReadOnlyList<ITxSigner> signers)
	{
		var required = SignedTxMsg.RequiredWitnesses(msg);
		if (required == null)
		{
			var open = new WitnessSlot[signers.Count];
			for (var i = 0; i < signers.Count; i++)
				open[i] = new WitnessSlot(new Bytes32(signers[i].PublicKey), signers[i]);
			return open;
		}
		if (msg.type == TxTypes.Phantasma_Raw)
		{
			if (signers.Count != 0)
				throw new ArgumentException("Phantasma_Raw transactions carry no witnesses", nameof(signers));
			return Array.Empty<WitnessSlot>();
		}

		var byAddress = new Dictionary<Bytes32, ITxSigner>();
		foreach (var signer in signers)
			byAddress[new Bytes32(signer.PublicKey)] = signer;
		var slots = new WitnessSlot[required.Length];
		for (var i = 0; i < required.Length; i++)
		{
			if (!byAddress.TryGetValue(required[i], out var signer))
				throw new ArgumentException($"No signer for witness {required[i].ToHex()}", nameof(signers));
			slots[i] = new WitnessSlot(required[i], signer);
		}
		foreach (var address in byAddress.Keys)
		{
			if (Array.IndexOf(required, address) < 0)
				throw new ArgumentException($"Signer {address.ToHex()} is not a witness of this transaction", nameof(signers));
		}
		return slots;
	}
}

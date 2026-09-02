using PhantasmaPhoenix.Core;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

/// <summary>
/// A transaction message with its witness signatures, in the envelope layout the chain reads.
/// <list type="bullet">
/// <item>A native single-witness type appends one bare 64-byte signature by <c>gasFrom</c>.</item>
/// <item>A <c>_GasPayer</c> type appends two bare signatures. The gas payer (<c>gasFrom</c>) comes
/// first and the token owner second. The owner is the message's <c>from</c>, and the node resolves
/// that address from the message itself.</item>
/// <item>Call, Call_Multi, Trade and Phantasma append a length-prefixed array of witnesses. Each
/// entry is an address and a signature, and the caller composes the array.</item>
/// <item>Phantasma_Raw carries no witnesses.</item>
/// </list>
/// </summary>
public struct SignedTxMsg : ICarbonBlob
{
	public TxMsg msg;
	public Witness[] witnesses;

	/// <summary>
	/// Returns the addresses whose signatures the envelope of <paramref name="msg"/> must carry, in
	/// envelope order. This works for the types whose witness set the message fixes. It returns null
	/// for the witness-array types, which are Call, Call_Multi, Trade and Phantasma. The caller chooses
	/// the witnesses of those four.
	/// </summary>
	public static Bytes32[]? RequiredWitnesses(in TxMsg msg)
	{
		switch (msg.type)
		{
			case TxTypes.TransferFungible:
			case TxTypes.TransferNonFungible_Single:
			case TxTypes.TransferNonFungible_Multi:
			case TxTypes.MintFungible:
			case TxTypes.BurnFungible:
			case TxTypes.MintNonFungible:
			case TxTypes.BurnNonFungible:
				return new[] { msg.gasFrom };
			case TxTypes.TransferFungible_GasPayer:
			case TxTypes.TransferNonFungible_Single_GasPayer:
			case TxTypes.TransferNonFungible_Multi_GasPayer:
			case TxTypes.BurnFungible_GasPayer:
			case TxTypes.BurnNonFungible_GasPayer:
				return new[] { msg.gasFrom, GasPayerOwner(msg) };
			case TxTypes.Phantasma_Raw:
				return Array.Empty<Bytes32>();
			default:
				return null;
		}
	}

	/// <summary>
	/// Returns the size in bytes of <paramref name="msg"/> once signed. That is the envelope the block
	/// carries and gas model v2 bills. No key is needed. Signatures are fixed-width, so zero-filled
	/// placeholder witnesses serialize to exactly the signed length.
	/// <para>
	/// The witness set is the one the message requires. For the witness-array types, which are Call,
	/// Call_Multi, Trade and Phantasma, pass how many witnesses will sign.
	/// </para>
	/// </summary>
	public static int EnvelopeBytes(in TxMsg msg, int? witnessCount = null)
	{
		var required = RequiredWitnesses(msg);
		if (required != null && witnessCount.HasValue && witnessCount.Value != required.Length)
		{
			throw new ArgumentException($"{msg.type} carries {required.Length} witness(es), not {witnessCount.Value}", nameof(witnessCount));
		}
		// The gas payer is always one of the witnesses of a witness-array message, so the placeholder
		// set names it. Addresses are fixed-width, so this does not change the size being measured. It
		// keeps the sizing path honest about the envelope it predicts, and it lets Write below enforce
		// the payer's presence without rejecting its own placeholders.
		var addresses = required ?? OpenWitnessPlaceholders(msg, witnessCount ?? 1);
		var placeholders = new Witness[addresses.Length];
		for (var i = 0; i < addresses.Length; i++)
		{
			placeholders[i] = new Witness { address = addresses[i], signature = new Bytes64() };
		}
		return CarbonBlob.Serialize(new SignedTxMsg { msg = msg, witnesses = placeholders }).Length;
	}

	public void Write(BinaryWriter w)
	{
		w.Write(msg);
		switch (msg.type)
		{
			case TxTypes.TransferFungible:
			case TxTypes.TransferNonFungible_Single:
			case TxTypes.TransferNonFungible_Multi:
			case TxTypes.MintFungible:
			case TxTypes.BurnFungible:
			case TxTypes.MintNonFungible:
			case TxTypes.BurnNonFungible:
				if (witnesses.Length != 1)
					throw new InvalidOperationException($"{msg.type} carries exactly one witness, not {witnesses.Length}");
				if (!witnesses[0].address.Equals(msg.gasFrom))
					throw new InvalidOperationException("Witness address mismatch: the single witness must be the gas payer");
				w.Write<Bytes64>(witnesses[0].signature);
				break;

			case TxTypes.TransferFungible_GasPayer:
			case TxTypes.TransferNonFungible_Single_GasPayer:
			case TxTypes.TransferNonFungible_Multi_GasPayer:
			case TxTypes.BurnFungible_GasPayer:
			case TxTypes.BurnNonFungible_GasPayer:
				if (witnesses.Length != 2)
					throw new InvalidOperationException($"{msg.type} carries exactly two witnesses, not {witnesses.Length}");
				if (!witnesses[0].address.Equals(msg.gasFrom))
					throw new InvalidOperationException("Gas witness address mismatch: the first witness must be the gas payer");
				if (!witnesses[1].address.Equals(GasPayerOwner(msg)))
					throw new InvalidOperationException("From witness address mismatch: the second witness must be the token owner");
				w.Write<Bytes64>(witnesses[0].signature);
				w.Write<Bytes64>(witnesses[1].signature);
				break;

			case TxTypes.Call:
			case TxTypes.Call_Multi:
			case TxTypes.Trade:
			case TxTypes.Phantasma:
				// The witness set of these types is the caller's to choose, with one rule the node
				// enforces: the account paying the gas must be among the signers, or the transaction
				// is rejected as "not signed by gas payer". Catching it here names the mistake
				// instead of spending a round trip on it.
				if (!PayerIsWitness())
					throw new InvalidOperationException("The gas payer must be one of the witnesses");
				w.WriteArray(witnesses);
				break;

			case TxTypes.Phantasma_Raw:
				if (witnesses.Length != 0)
					throw new InvalidOperationException("Phantasma_Raw transactions carry no witnesses");
				break;

			default:
				throw new InvalidOperationException($"Unsupported transaction type {msg.type}");
		}
	}

	public void Read(BinaryReader r)
	{
		r.Read(out msg);
		switch (msg.type)
		{
			case TxTypes.TransferFungible:
			case TxTypes.TransferNonFungible_Single:
			case TxTypes.TransferNonFungible_Multi:
			case TxTypes.MintFungible:
			case TxTypes.BurnFungible:
			case TxTypes.MintNonFungible:
			case TxTypes.BurnNonFungible:
				witnesses = new Witness[]{
					new Witness { address = msg.gasFrom, signature = r.Read<Bytes64>() }
				};
				break;

			case TxTypes.TransferFungible_GasPayer:
			case TxTypes.TransferNonFungible_Single_GasPayer:
			case TxTypes.TransferNonFungible_Multi_GasPayer:
			case TxTypes.BurnFungible_GasPayer:
			case TxTypes.BurnNonFungible_GasPayer:
				witnesses = new Witness[]{
					new Witness { address = msg.gasFrom, signature = r.Read<Bytes64>() },
					new Witness { address = GasPayerOwner(msg), signature = r.Read<Bytes64>() },
				};
				break;

			case TxTypes.Call:
			case TxTypes.Call_Multi:
			case TxTypes.Trade:
			case TxTypes.Phantasma:
				witnesses = r.ReadArray<Witness>();
				break;

			case TxTypes.Phantasma_Raw:
				witnesses = Array.Empty<Witness>();
				break;

			default:
				throw new InvalidOperationException($"Unsupported transaction type {msg.type}");
		}
	}

	private bool PayerIsWitness()
	{
		foreach (var witness in witnesses)
		{
			if (witness.address.Equals(msg.gasFrom))
				return true;
		}
		return false;
	}

	// Placeholder addresses for sizing a witness-array envelope: the gas payer, which the node requires
	// to be a witness, then anonymous fillers for the rest. Only the count affects the size.
	private static Bytes32[] OpenWitnessPlaceholders(in TxMsg msg, int witnessCount)
	{
		if (witnessCount < 1)
			throw new ArgumentOutOfRangeException(nameof(witnessCount), $"{msg.type} needs at least one witness, not {witnessCount}");
		var addresses = new Bytes32[witnessCount];
		addresses[0] = msg.gasFrom;
		for (var i = 1; i < witnessCount; i++)
			addresses[i] = new Bytes32();
		return addresses;
	}

	// The token owner of a gas-payer message: the account whose tokens move, as opposed to gasFrom,
	// which only pays. Every gas-payer message type carries it as `from`.
	private static Bytes32 GasPayerOwner(in TxMsg msg)
	{
		switch (msg.type)
		{
			case TxTypes.TransferFungible_GasPayer: return ((TxMsgTransferFungible_GasPayer)msg.msg).from;
			case TxTypes.TransferNonFungible_Single_GasPayer: return ((TxMsgTransferNonFungible_Single_GasPayer)msg.msg).from;
			case TxTypes.TransferNonFungible_Multi_GasPayer: return ((TxMsgTransferNonFungible_Multi_GasPayer)msg.msg).from;
			case TxTypes.BurnFungible_GasPayer: return ((TxMsgBurnFungible_GasPayer)msg.msg).from;
			case TxTypes.BurnNonFungible_GasPayer: return ((TxMsgBurnNonFungible_GasPayer)msg.msg).from;
			default: throw new ArgumentException($"{msg.type} is not a gas-payer transaction type", nameof(msg));
		}
	}
}

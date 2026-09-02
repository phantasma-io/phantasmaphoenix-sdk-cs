using PhantasmaPhoenix.Cryptography;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

/// <summary>Options of the <c>BuildTxAndSign*</c> conveniences: how to plan the fee, or what to write instead.</summary>
public sealed class PlanAndSignOptions : FeePlanOptions
{
	/// <inheritdoc cref="TxLimits.MaxGas"/>
	public ulong? MaxGas { get; set; }
	/// <inheritdoc cref="TxLimits.MaxData"/>
	public ulong? MaxData { get; set; }
	/// <inheritdoc cref="TxLimits.Expiry"/>
	public long? Expiry { get; set; }

	/// <summary>The limits half of these options, in the shape the builders take.</summary>
	public TxLimits Limits => new() { MaxGas = MaxGas, MaxData = MaxData, Expiry = Expiry };
}

public static class PlanAndSign
{
	/// <summary>
	/// Plans a freshly built message against <paramref name="config"/> - unless the caller fixed
	/// <c>MaxGas</c> themselves - and signs it with in-memory keys. The convenience behind every
	/// <c>BuildTxAndSign*</c> helper; a wallet with an external signer plans with
	/// <see cref="FeePlanner.Plan"/> and signs with <see cref="TxMsgSigner.SignWithAsync"/>.
	/// </summary>
	public static byte[] WithKeys(TxMsg msg, IKeyPair[] keys, in GasConfig config, PlanAndSignOptions? options = null)
	{
		if (keys == null) throw new ArgumentNullException(nameof(keys));
		// Whether the fee is already settled is read from the MESSAGE: the builders are what write the
		// caller's limits into it, so the message is the one place that is right for every helper. The
		// same rule as PhantasmaAPI.SendTransactionAsync.
		if (msg.maxGas != 0)
			return TxMsgSigner.SignAndSerializeWithKeys(msg, keys);
		options ??= new PlanAndSignOptions();
		// Only the witness-array types take their witness count from the caller, and these keys are that
		// caller's answer; for every other type the message fixes its own slots and one key may fill two
		// of them, so passing a count would contradict the message.
		var planOptions = options.CloneOptions();
		if (!planOptions.WitnessCount.HasValue && SignedTxMsg.RequiredWitnesses(msg) == null)
			planOptions.WitnessCount = keys.Length;
		var planned = FeePlanner.Plan(msg, config, planOptions).Apply(msg);
		return TxMsgSigner.SignAndSerializeWithKeys(planned, keys);
	}
}

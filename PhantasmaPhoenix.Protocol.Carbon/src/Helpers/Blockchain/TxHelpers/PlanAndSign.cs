using PhantasmaPhoenix.Cryptography;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

/// <summary>Options of the <c>BuildTxAndSign*</c> helpers. They say how to plan the fee, or what to
/// write in place of a plan.</summary>
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
	/// Plans a freshly built message against <paramref name="config"/> and signs it with in-memory
	/// keys. A message whose <c>MaxGas</c> the caller already fixed is signed as it is.
	/// <para>
	/// This is the method behind every <c>BuildTxAndSign*</c> helper. A wallet with an external signer
	/// plans with <see cref="FeePlanner.Plan"/> and signs with
	/// <see cref="TxMsgSigner.SignWithAsync"/> instead.
	/// </para>
	/// </summary>
	public static byte[] WithKeys(TxMsg msg, IKeyPair[] keys, in GasConfig config, PlanAndSignOptions? options = null)
	{
		if (keys == null) throw new ArgumentNullException(nameof(keys));
		// Whether the fee is already settled is read from the MESSAGE. The builders write the caller's
		// limits into it, so the message is the one place that is right for every helper.
		// PhantasmaAPI.SendTransactionAsync follows the same rule.
		if (msg.maxGas != 0)
			return TxMsgSigner.SignAndSerializeWithKeys(msg, keys);
		options ??= new PlanAndSignOptions();
		// Only the witness-array types take their witness count from the caller, and these keys are that
		// caller's answer. Every other type fixes its own slots in the message, and one key may fill two
		// of them, so passing a count would contradict the message.
		var planOptions = options.Clone();
		if (!planOptions.WitnessCount.HasValue && SignedTxMsg.RequiredWitnesses(msg) == null)
			planOptions.WitnessCount = keys.Length;
		var planned = FeePlanner.Plan(msg, config, planOptions).Apply(msg);
		return TxMsgSigner.SignAndSerializeWithKeys(planned, keys);
	}
}

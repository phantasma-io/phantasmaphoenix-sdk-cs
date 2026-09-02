using PhantasmaPhoenix.Core;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

/// <summary>A fee plan in the units a person reads. Gas is shown in KCAL and the storage deposit in
/// SOUL.</summary>
public sealed class FeePlanSummary
{
	/// <summary>Decimals of the chain's gas token, KCAL.</summary>
	public const uint GasTokenDecimals = 10;

	/// <summary>Decimals of the chain's data token, SOUL.</summary>
	public const uint DataTokenDecimals = 8;

	/// <summary>What the transaction will cost in gas, as a decimal KCAL amount.</summary>
	public string GasBill { get; }

	/// <summary>The gas offer written into the transaction. The difference to <see cref="GasBill"/> is
	/// refunded.</summary>
	public string GasOffer { get; }

	/// <summary>
	/// The storage deposit the transaction may take, as a decimal SOUL amount. The chain escrows it
	/// while the transaction settles and refunds it when the rows it paid for are deleted. A wallet
	/// shows it apart from the fee, as a refundable deposit.
	/// </summary>
	public string StorageCeiling { get; }

	private FeePlanSummary(string gasBill, string gasOffer, string storageCeiling)
	{
		GasBill = gasBill;
		GasOffer = gasOffer;
		StorageCeiling = storageCeiling;
	}

	/// <summary>Renders a plan in the units a person reads. Pass the decimals of a chain whose gas or
	/// data token differ.</summary>
	public static FeePlanSummary Summarize(FeePlan plan, uint gasDecimals = GasTokenDecimals, uint dataDecimals = DataTokenDecimals)
	{
		if (plan == null) throw new ArgumentNullException(nameof(plan));
		return new FeePlanSummary(
			UnitConversion.ToDecimalString(plan.ExpectedGasBill.ToString(), gasDecimals),
			UnitConversion.ToDecimalString(plan.MaxGas.ToString(), gasDecimals),
			UnitConversion.ToDecimalString(plan.MaxData.ToString(), dataDecimals));
	}
}

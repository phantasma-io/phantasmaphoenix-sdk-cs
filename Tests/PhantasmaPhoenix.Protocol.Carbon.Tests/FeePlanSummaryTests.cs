using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

public class FeePlanSummaryTests
{
	private static FeePlan Plan(ulong maxGas, ulong maxData, ulong bill) =>
		new(new[] { NativeFeeKind.TransferFungible }, true, 170, new NativeFeeEstimate(maxGas, maxData, bill, 1, 0));

	// 42,600,000 kcal-base is 0.00426 KCAL and 200,000 SOUL atoms is 0.002 SOUL: the atoms a wallet
	// would otherwise show a user, rendered in the units the user thinks in, without trailing zeros.
	[Fact]
	public void Renders_the_plan_in_KCAL_and_SOUL()
	{
		var summary = FeePlanSummary.Summarize(Plan(42_600_000, 200_000, 42_600_000));
		summary.GasBill.ShouldBe("0.00426");
		summary.GasOffer.ShouldBe("0.00426");
		summary.StorageCeiling.ShouldBe("0.002");

		var floored = FeePlanSummary.Summarize(Plan(10_000_000_000, 0, 10_000_000));
		floored.GasBill.ShouldBe("0.001");
		floored.GasOffer.ShouldBe("1");
		floored.StorageCeiling.ShouldBe("0");
	}

	[Fact]
	public void Takes_the_decimals_of_a_chain_whose_gas_or_data_token_differ()
	{
		var summary = FeePlanSummary.Summarize(Plan(1_500, 2_500, 1_500), gasDecimals: 3, dataDecimals: 2);
		summary.GasBill.ShouldBe("1.5");
		summary.StorageCeiling.ShouldBe("25");
	}
}

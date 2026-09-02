using Newtonsoft.Json;
using PhantasmaPhoenix.RPC.Models;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.RPC.Tests;

// estimateTransaction response decoding: the node serializes 64-bit amounts as decimal strings
// (JSON-number precision), and a completed estimate must convert into the same FeeQuote
// struct Tier-1 produces so wallet code consumes both tiers identically.
public class EstimateTransactionResultTests
{
	// Completed dry run: recommendations present, no abort. recommendedMaxGas deliberately
	// exceeds 2^53 to pin the strings-not-numbers decision.
	private const string CompletedJson = """
	{
	  "wouldAbort": false,
	  "abortReason": "",
	  "gasBillKcalBase": "10000000",
	  "dataRows": "1",
	  "dataEscrowAtoms": "200000",
	  "dataRefundAtoms": "0",
	  "recommendedMaxGas": "100000000000000000",
	  "recommendedMaxData": "400000"
	}
	""";

	// Aborted dry run: the settled abort bill is still reported (aborts pay), recommendations are
	// zero and the reason text explains the abort.
	private const string AbortedJson = """
	{
	  "wouldAbort": true,
	  "abortReason": "gas fees [gas=3125 max=40]",
	  "gasBillKcalBase": "40",
	  "dataRows": "0",
	  "dataEscrowAtoms": "0",
	  "dataRefundAtoms": "0",
	  "recommendedMaxGas": "0",
	  "recommendedMaxData": "0"
	}
	""";

	[Fact]
	public void Completed_response_decodes_and_converts_to_fee_estimate()
	{
		var result = JsonConvert.DeserializeObject<EstimateTransactionResult>(CompletedJson);

		result.ShouldNotBeNull();
		result.WouldAbort.ShouldBeFalse();
		result.AbortReason.ShouldBe("");
		result.GasBillKcalBase.ShouldBe("10000000");
		result.DataRows.ShouldBe("1");
		result.DataEscrowAtoms.ShouldBe("200000");
		result.DataRefundAtoms.ShouldBe("0");

		var estimate = result.ToFeeQuote();
		estimate.MaxGas.ShouldBe(100000000000000000UL);
		estimate.MaxData.ShouldBe(400000UL);
		estimate.ExpectedGasBill.ShouldBe(10000000UL);
	}

	[Fact]
	public void Aborted_response_decodes_but_refuses_fee_estimate_conversion()
	{
		var result = JsonConvert.DeserializeObject<EstimateTransactionResult>(AbortedJson);

		result.ShouldNotBeNull();
		result.WouldAbort.ShouldBeTrue();
		result.AbortReason.ShouldBe("gas fees [gas=3125 max=40]");
		// Aborted txs still settle a bill (clamped at the declared maxGas) - the field stays readable.
		result.GasBillKcalBase.ShouldBe("40");

		// No recommendations exist for an aborted run; converting must fail loudly, not yield zeros
		// a wallet could sign with.
		var error = Should.Throw<InvalidOperationException>(() => result.ToFeeQuote());
		error.Message.ShouldContain("gas fees");
	}

	[Fact]
	public void Missing_numeric_field_fails_conversion_loudly()
	{
		// A malformed server response (lost field) must not silently become a zero ceiling.
		var result = JsonConvert.DeserializeObject<EstimateTransactionResult>("""{ "wouldAbort": false }""");

		result.ShouldNotBeNull();
		Should.Throw<FormatException>(() => result.ToFeeQuote());
	}
}

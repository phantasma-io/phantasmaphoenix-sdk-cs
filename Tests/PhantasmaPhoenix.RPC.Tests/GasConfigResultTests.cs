using Newtonsoft.Json;
using PhantasmaPhoenix.RPC.Models;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.RPC.Tests;

// getGasConfig response decoding: the node serializes 64-bit config values as decimal strings
// (JSON-number precision) and omits the v2 tail fields for version-0 configs. ToGasConfig() must
// produce the exact wire struct the Tier-1 estimator consumes.
public class GasConfigResultTests
{
	// Shape currently served by a v1 (pre-flip) chain: no v2 fields at all.
	private const string V1Json = """
	{
	  "gasModelVersion": 1,
	  "blockRateTarget": 2000,
	  "expiryWindow": 90000,
	  "gasConfig": {
	    "version": 0,
	    "maxNameLength": 32,
	    "maxTokenSymbolLength": 10,
	    "feeShift": 0,
	    "maxStructureSize": 65536,
	    "feeMultiplier": "10000",
	    "gasTokenId": "2",
	    "dataTokenId": "1",
	    "minimumGasOffer": "10",
	    "dataEscrowPerRow": "2",
	    "gasFeeTransfer": "10",
	    "gasFeeQuery": "2",
	    "gasFeeCreateTokenBase": "10000000000",
	    "gasFeeCreateTokenSymbol": "10000000000",
	    "gasFeeCreateTokenSeries": "2500000000",
	    "gasFeePerByte": "250000",
	    "gasFeeRegisterName": "10000000000000",
	    "gasBurnRatioMul": "1",
	    "gasBurnRatioShift": 0
	  }
	}
	""";

	// Post-flip shape: version 1 config with the full v2 tail (values from the activation spec;
	// policyFeeRegisterName deliberately exceeds 2^53 to pin the strings-not-numbers decision).
	private const string V2Json = """
	{
	  "gasModelVersion": 2,
	  "blockRateTarget": 2000,
	  "expiryWindow": 90000,
	  "unitsPerBlockDataByte": 25,
	  "gasConfig": {
	    "version": 1,
	    "maxNameLength": 32,
	    "maxTokenSymbolLength": 10,
	    "feeShift": 0,
	    "maxStructureSize": 65536,
	    "feeMultiplier": "10000",
	    "gasTokenId": "2",
	    "dataTokenId": "1",
	    "minimumGasOffer": "10",
	    "dataEscrowPerRow": "200000",
	    "gasFeeTransfer": "10",
	    "gasFeeQuery": "2",
	    "gasFeeCreateTokenBase": "10000000000",
	    "gasFeeCreateTokenSymbol": "10000000000",
	    "gasFeeCreateTokenSeries": "2500000000",
	    "gasFeePerByte": "250000",
	    "gasFeeRegisterName": "10000000000000",
	    "gasBurnRatioMul": "1",
	    "gasBurnRatioShift": 0,
	    "minimumGasBill": "10000000",
	    "gasProducerRatioMul": "0",
	    "gasProducerRatioShift": 0,
	    "gasDappRatioMul": "0",
	    "gasDappRatioShift": 0,
	    "policyFeeCreateTokenBase": "100000000000000",
	    "policyFeeCreateTokenSymbol": "100000000000000",
	    "policyFeeCreateTokenSeries": "25000000000000",
	    "policyFeeRegisterName": "100000000000000000",
	    "legacyDataEscrowPerRow": "2"
	  }
	}
	""";

	[Fact]
	public void V1_response_decodes_and_maps()
	{
		var result = JsonConvert.DeserializeObject<GasConfigResult>(V1Json);

		result.ShouldNotBeNull();
		result!.GasModelVersion.ShouldBe(1u);
		result.BlockRateTarget.ShouldBe(2000u);
		result.ExpiryWindow.ShouldBe(90000u);
		result.UnitsPerBlockDataByte.ShouldBeNull();

		var config = result.ToGasConfig();
		config.version.ShouldBe((byte)0);
		config.HasGasModelV2.ShouldBeFalse();
		config.feeMultiplier.ShouldBe(10_000UL);
		config.gasFeePerByte.ShouldBe(250_000UL);
		// v2 fields absent from the wire must map to zero, not garbage.
		config.minimumGasBill.ShouldBe(0UL);
		config.policyFeeRegisterName.ShouldBe(0UL);
	}

	[Fact]
	public void V2_response_decodes_and_maps()
	{
		var result = JsonConvert.DeserializeObject<GasConfigResult>(V2Json);

		result.ShouldNotBeNull();
		result!.GasModelVersion.ShouldBe(2u);
		result.UnitsPerBlockDataByte.ShouldBe(25u);

		var config = result.ToGasConfig();
		config.version.ShouldBe((byte)1);
		config.HasGasModelV2.ShouldBeTrue();
		config.dataEscrowPerRow.ShouldBe(200_000UL);
		config.minimumGasBill.ShouldBe(10_000_000UL);
		// Above-2^53 value survives exactly because it rides a string.
		config.policyFeeRegisterName.ShouldBe(100_000_000_000_000_000UL);
		config.legacyDataEscrowPerRow.ShouldBe(2UL);
	}

	// A response claiming gas model v2 but missing tail fields must fail loudly - estimating
	// fees from silently zeroed v2 prices would produce rejected transactions.
	[Fact]
	public void V2_response_with_missing_tail_field_throws()
	{
		var broken = V2Json.Replace("\"minimumGasBill\": \"10000000\",", "");
		var result = JsonConvert.DeserializeObject<GasConfigResult>(broken);

		result.ShouldNotBeNull();
		Should.Throw<FormatException>(() => result!.ToGasConfig());
	}

	[Fact]
	public void Missing_gas_config_section_throws()
	{
		var result = JsonConvert.DeserializeObject<GasConfigResult>("""{ "gasModelVersion": 1 }""");

		result.ShouldNotBeNull();
		Should.Throw<InvalidOperationException>(() => result!.ToGasConfig());
	}
}

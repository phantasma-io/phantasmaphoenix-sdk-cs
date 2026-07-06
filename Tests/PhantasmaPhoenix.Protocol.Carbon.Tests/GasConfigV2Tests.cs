using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

// Wire-format tests for the gas-model-v2 GasConfig extension. The chain serializes the 10 v2
// fields only for version >= 1; the version-0 image is frozen forever (historical replay), so
// these tests pin both layouts and the truncation failure mode.
public class GasConfigV2Tests
{
	// The mainnet v1 values (feeMultiplier 10000, transfer 10 units, byte fee 250000, escrow 2).
	private static GasConfig LiveV1Config() => new GasConfig
	{
		version = 0,
		maxNameLength = 32,
		maxTokenSymbolLength = 10,
		feeShift = 0,
		maxStructureSize = 65536,
		feeMultiplier = 10_000,
		gasTokenId = 2,
		dataTokenId = 1,
		minimumGasOffer = 10,
		dataEscrowPerRow = 2,
		gasFeeTransfer = 10,
		gasFeeQuery = 2,
		gasFeeCreateTokenBase = 10_000_000_000,
		gasFeeCreateTokenSymbol = 10_000_000_000,
		gasFeeCreateTokenSeries = 2_500_000_000,
		gasFeePerByte = 250_000,
		gasFeeRegisterName = 10_000_000_000_000,
		gasBurnRatioMul = 1,
		gasBurnRatioShift = 0
	};

	// The spec 2.6 activation-package values for the v2 tail.
	private static GasConfig V2Config()
	{
		var config = LiveV1Config();
		config.version = 1;
		config.dataEscrowPerRow = 200_000;
		config.minimumGasBill = 10_000_000;
		config.gasProducerRatioMul = 0;
		config.gasProducerRatioShift = 0;
		config.gasDappRatioMul = 0;
		config.gasDappRatioShift = 0;
		config.policyFeeCreateTokenBase = 100_000_000_000_000;
		config.policyFeeCreateTokenSymbol = 100_000_000_000_000;
		config.policyFeeCreateTokenSeries = 25_000_000_000_000;
		config.policyFeeRegisterName = 100_000_000_000_000_000;
		config.legacyDataEscrowPerRow = 2;
		return config;
	}

	// Version-0 configs must keep the exact pre-v2 wire size (113 bytes: 4x u8 + u32 + 13x u64 +
	// u8); any growth would corrupt every historical block image.
	[Fact]
	public void V0_image_keeps_legacy_113_byte_layout()
	{
		var bytes = CarbonBlob.Serialize(LiveV1Config());
		bytes.Length.ShouldBe(113);
	}

	// A version>=1 config appends the 66-byte v2 tail (8x u64 + 2x u8) after an unchanged head
	// encoding - the tail is a pure wire extension, it must not disturb the first 113 bytes.
	[Fact]
	public void V2_image_appends_66_byte_tail()
	{
		var v2Bytes = CarbonBlob.Serialize(V2Config());
		v2Bytes.Length.ShouldBe(179);

		var v0Twin = V2Config();
		v0Twin.version = 0; // same head values, version-0 layout
		var v0Bytes = CarbonBlob.Serialize(v0Twin);
		v0Bytes.Length.ShouldBe(113);

		v2Bytes[0].ShouldBe((byte)1);
		v0Bytes[0].ShouldBe((byte)0);
		for (int i = 1; i < 113; i++)
			v2Bytes[i].ShouldBe(v0Bytes[i], $"head byte {i} diverged");
	}

	[Fact]
	public void V2_roundtrip_preserves_all_fields()
	{
		var original = V2Config();
		var decoded = CarbonBlob.New<GasConfig>(CarbonBlob.Serialize(original));

		decoded.version.ShouldBe((byte)1);
		decoded.HasGasModelV2.ShouldBeTrue();
		decoded.dataEscrowPerRow.ShouldBe(200_000UL);
		decoded.minimumGasBill.ShouldBe(10_000_000UL);
		decoded.policyFeeCreateTokenBase.ShouldBe(100_000_000_000_000UL);
		decoded.policyFeeCreateTokenSymbol.ShouldBe(100_000_000_000_000UL);
		decoded.policyFeeCreateTokenSeries.ShouldBe(25_000_000_000_000UL);
		decoded.policyFeeRegisterName.ShouldBe(100_000_000_000_000_000UL);
		decoded.legacyDataEscrowPerRow.ShouldBe(2UL);
		decoded.gasProducerRatioMul.ShouldBe(0UL);
		decoded.gasDappRatioMul.ShouldBe(0UL);
	}

	// Reading a version-0 image must zero the v2 fields even if the buffer under the reader is
	// dirty - consumers must never see stale tail values on a v1 chain.
	[Fact]
	public void V0_roundtrip_zeroes_v2_fields()
	{
		var decoded = CarbonBlob.New<GasConfig>(CarbonBlob.Serialize(LiveV1Config()));

		decoded.version.ShouldBe((byte)0);
		decoded.HasGasModelV2.ShouldBeFalse();
		decoded.minimumGasBill.ShouldBe(0UL);
		decoded.policyFeeCreateTokenBase.ShouldBe(0UL);
		decoded.legacyDataEscrowPerRow.ShouldBe(0UL);
	}

	// A version>=1 image truncated to the version-0 length must FAIL to parse, never silently
	// produce a config with zeroed v2 prices (that would mean free product actions).
	[Fact]
	public void Truncated_v2_image_fails_to_parse()
	{
		var full = CarbonBlob.Serialize(V2Config());
		var truncated = new byte[113];
		Array.Copy(full, truncated, 113);

		Should.Throw<Exception>(() => CarbonBlob.New<GasConfig>(truncated));
	}

	// The strict no-trailing-bytes decode must reject a v0 head followed by an unexpected tail:
	// old parsers reading a v2 chain fail loudly instead of misreading fee prices.
	[Fact]
	public void V0_image_with_trailing_tail_fails_strict_parse()
	{
		var v2 = CarbonBlob.Serialize(V2Config());
		v2[0] = 0; // pretend the config claims version 0 while carrying a tail

		Should.Throw<Exception>(() => CarbonBlob.New<GasConfig>(v2));
	}
}

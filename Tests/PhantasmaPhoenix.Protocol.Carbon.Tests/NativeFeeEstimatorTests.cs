using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

// Tier-1 fee calculator tests. Expected numbers are hand-derived from the chain billing formula
// (bill = work-units and byte fee through the feeMultiplier/feeShift knob, plus v2 policy fees
// and the v2 minimum-bill floor), pinned as constants so any formula regression fails loudly.
public class NativeFeeEstimatorTests
{
	// Live mainnet v1 values: multiplier 10000, shift 0, transfer 10 units, byte fee 250000
	// kcal-base, minimum offer 10, escrow 2 atoms/row.
	private static GasConfig V1Config() => new GasConfig
	{
		version = 0,
		maxNameLength = 32,
		maxTokenSymbolLength = 10,
		feeShift = 0,
		feeMultiplier = 10_000,
		minimumGasOffer = 10,
		dataEscrowPerRow = 2,
		gasFeeTransfer = 10,
		gasFeeQuery = 2,
		gasFeeCreateTokenBase = 10_000_000_000,
		gasFeeCreateTokenSymbol = 10_000_000_000,
		gasFeeCreateTokenSeries = 2_500_000_000,
		gasFeePerByte = 250_000,
		gasFeeRegisterName = 10_000_000_000_000,
		gasBurnRatioMul = 1
	};

	// Spec 2.6 activation values on top of the same knob settings.
	private static GasConfig V2Config()
	{
		var config = V1Config();
		config.version = 1;
		config.dataEscrowPerRow = 200_000;
		config.legacyDataEscrowPerRow = 2;
		config.minimumGasBill = 10_000_000;
		config.policyFeeCreateTokenBase = 100_000_000_000_000;
		config.policyFeeCreateTokenSymbol = 100_000_000_000_000;
		config.policyFeeCreateTokenSeries = 25_000_000_000_000;
		config.policyFeeRegisterName = 100_000_000_000_000_000;
		return config;
	}

	// v1 transfer with an existing recipient row: bill is the pure work term 10 * 10000.
	[Fact]
	public void V1_transfer_existing_recipient_bill_is_work_only()
	{
		var estimate = NativeFeeEstimator.EstimateNativeFee(NativeFeeKind.TransferFungible, V1Config(),
			new NativeFeeParams { FreshRows = 0 });

		estimate.ExpectedGasBill.ShouldBe(100_000UL);
		// stdFee shape: 2x min offer + work + flat 1 KiB byte allowance.
		estimate.MaxGas.ShouldBe(10UL * 2 + 100_000 + 1024 * 250_000);
		estimate.MaxData.ShouldBe(0UL);
	}

	// v1 transfer default (worst case: 1 fresh row): the row quantum joins the byte fee and the
	// escrow shows up in MaxData at the v1 price.
	[Fact]
	public void V1_transfer_defaults_include_one_fresh_row()
	{
		var estimate = NativeFeeEstimator.EstimateNativeFee(NativeFeeKind.TransferFungible, V1Config());

		estimate.ExpectedGasBill.ShouldBe(100_000UL + 1 * 250_000);
		estimate.MaxData.ShouldBe(2UL);
	}

	// v2 transfer, default envelope 512 + 1 fresh row: blockData 513 bytes-equivalent -> 12825
	// byte units + 10 work units = 12835 units * 10000 = 128_350_000 kcal-base (above the 1e7
	// floor). MaxGas adds the deterministic 25% headroom; MaxData prices the row at 200000.
	[Fact]
	public void V2_transfer_default_envelope_bill()
	{
		var estimate = NativeFeeEstimator.EstimateNativeFee(NativeFeeKind.TransferFungible, V2Config());

		estimate.ExpectedGasBill.ShouldBe(128_350_000UL);
		estimate.MaxGas.ShouldBe(128_350_000UL + 128_350_000UL / 4);
		estimate.MaxData.ShouldBe(200_000UL);
	}

	// v2 exact envelope: a measured 250-byte native transfer to an existing recipient bills
	// (10 + 250*25) * 10000 = 62_600_000.
	[Fact]
	public void V2_transfer_exact_envelope_bill()
	{
		var estimate = NativeFeeEstimator.EstimateNativeFee(NativeFeeKind.TransferFungible, V2Config(),
			new NativeFeeParams { EnvelopeBytes = 250, FreshRows = 0 });

		estimate.ExpectedGasBill.ShouldBe((10UL + 250 * 25) * 10_000);
	}

	// A tiny v2 tx can never bill below the consensus floor; the offer must also respect the
	// admission check maxGas >= minimumGasBill.
	[Fact]
	public void V2_floor_applies_to_small_bills()
	{
		var config = V2Config();
		config.minimumGasBill = 10_000_000_000; // exaggerated floor above the computed bill

		var estimate = NativeFeeEstimator.EstimateNativeFee(NativeFeeKind.TransferFungible, config,
			new NativeFeeParams { EnvelopeBytes = 250, FreshRows = 0 });

		estimate.ExpectedGasBill.ShouldBe(10_000_000_000UL);
		estimate.MaxGas.ShouldBeGreaterThanOrEqualTo(10_000_000_000UL);
	}

	// NFT transfers scale the work term per instance; under v2 each instance also recreates its
	// lookup row, so the escrow allowance is (count + 1) rows.
	[Fact]
	public void V2_nft_multi_transfer_scales_units_and_rows()
	{
		var estimate = NativeFeeEstimator.EstimateNativeFee(NativeFeeKind.TransferNonFungible, V2Config(),
			new NativeFeeParams { Count = 5, EnvelopeBytes = 300 });

		// work 5*10 units + bytes (300 envelope + 6 rows) * 25 units, all * 10000.
		estimate.ExpectedGasBill.ShouldBe((50UL + 306 * 25) * 10_000);
		estimate.MaxData.ShouldBe(6UL * 200_000);
	}

	// CreateToken under v1 charges unit-priced product fees through the multiplier: base 1e10 +
	// symbol 1e10 >> (len-1), a 4-char symbol pays 1e10 + 1.25e9 units.
	[Fact]
	public void V1_create_token_unit_fee()
	{
		var estimate = NativeFeeEstimator.EstimateNativeFee(NativeFeeKind.CreateToken, V1Config(),
			new NativeFeeParams { SymbolLength = 4, FreshRows = 0, EnvelopeBytes = 1000, PayloadBytes = 0 });

		// (1e10 + 1e10>>3) * 10000 = (10_000_000_000 + 1_250_000_000) * 10_000
		var work = (10_000_000_000UL + 1_250_000_000UL) * 10_000;
		estimate.ExpectedGasBill.ShouldBe(work); // byte fee: 0 payload, 0 events, 0 rows
	}

	// CreateToken under v2 pays the direct kcal-base policy fee (no multiplier) plus the byte
	// fee for its envelope; the policy magnitude equals the v1 price by design.
	[Fact]
	public void V2_create_token_policy_fee()
	{
		var estimate = NativeFeeEstimator.EstimateNativeFee(NativeFeeKind.CreateToken, V2Config(),
			new NativeFeeParams { SymbolLength = 4, FreshRows = 0, EnvelopeBytes = 1000 });

		var policy = 100_000_000_000_000UL + (100_000_000_000_000UL >> 3);
		var byteFee = 1000UL * 25 * 10_000;
		estimate.ExpectedGasBill.ShouldBe(policy + byteFee);
	}

	// RegisterName halves the price per character after the first, under both models.
	[Fact]
	public void Register_name_length_discount()
	{
		var v1 = NativeFeeEstimator.EstimateNativeFee(NativeFeeKind.RegisterName, V1Config(),
			new NativeFeeParams { NameLength = 8, FreshRows = 0, EnvelopeBytes = 300 });
		var v2 = NativeFeeEstimator.EstimateNativeFee(NativeFeeKind.RegisterName, V2Config(),
			new NativeFeeParams { NameLength = 8, FreshRows = 0, EnvelopeBytes = 300 });

		v1.ExpectedGasBill.ShouldBe((10_000_000_000_000UL >> 7) * 10_000);
		v2.ExpectedGasBill.ShouldBe((100_000_000_000_000_000UL >> 7) + 300UL * 25 * 10_000);
	}

	// The Script kind budgets a generous VM unit allowance (default 5000 exceeds every script in
	// mainnet history) instead of pretending opcode costs are closed-form.
	[Fact]
	public void Script_kind_budgets_vm_allowance()
	{
		var estimate = NativeFeeEstimator.EstimateNativeFee(NativeFeeKind.Script, V2Config(),
			new NativeFeeParams { EnvelopeBytes = 568, FreshRows = 0 });

		// (5000 vm units + (568 + 512 events) * 25) * 10000
		estimate.ExpectedGasBill.ShouldBe((5000UL + 1080 * 25) * 10_000);
	}

	// Envelope arithmetic mirrors SignedTxMsg: native kinds append bare 64-byte signatures,
	// call/script kinds append a length-prefixed 96-byte witness array.
	[Fact]
	public void Envelope_bytes_follow_witness_layout()
	{
		NativeFeeEstimator.EnvelopeBytesFor(NativeFeeKind.TransferFungible, 150).ShouldBe(150u + 64);
		NativeFeeEstimator.EnvelopeBytesFor(NativeFeeKind.TransferFungible, 150, 2).ShouldBe(150u + 128);
		NativeFeeEstimator.EnvelopeBytesFor(NativeFeeKind.CreateToken, 900).ShouldBe(900u + 4 + 96);
		NativeFeeEstimator.EnvelopeBytesFor(NativeFeeKind.Script, 500, 2).ShouldBe(500u + 4 + 192);
	}

	// Guard rails: impossible inputs are rejected instead of quoting fees for txs the chain
	// would never admit.
	[Fact]
	public void Invalid_inputs_throw()
	{
		Should.Throw<ArgumentOutOfRangeException>(() =>
			NativeFeeEstimator.EstimateNativeFee(NativeFeeKind.TransferFungible, V1Config(),
				new NativeFeeParams { Count = 0 }));
		Should.Throw<ArgumentOutOfRangeException>(() =>
			NativeFeeEstimator.EstimateNativeFee(NativeFeeKind.RegisterName, V1Config(),
				new NativeFeeParams { NameLength = 0 }));
		Should.Throw<ArgumentOutOfRangeException>(() =>
			NativeFeeEstimator.EstimateNativeFee(NativeFeeKind.CreateToken, V1Config(),
				new NativeFeeParams { SymbolLength = 11 })); // maxTokenSymbolLength is 10
	}

	// feeShift semantics: the chain clamps shifts >= 64 to a zero work delta; the estimator must
	// match rather than undercharge/overcharge.
	[Fact]
	public void Oversized_fee_shift_zeroes_scaled_terms()
	{
		var config = V1Config();
		config.feeShift = 64;

		var estimate = NativeFeeEstimator.EstimateNativeFee(NativeFeeKind.TransferFungible, config,
			new NativeFeeParams { FreshRows = 0 });

		// v1: work term zeroed, byte fee (raw kcal-base knob) unaffected by the shift.
		estimate.ExpectedGasBill.ShouldBe(0UL);
	}
}

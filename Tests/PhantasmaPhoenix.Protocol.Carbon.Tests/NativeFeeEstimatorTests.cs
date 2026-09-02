using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

// Tier-1 fee calculator tests. Every v2 expectation below is a bill a real transaction was actually
// charged on a gas-model-v2 network, read back from its settlement: a failure here means the SDK
// disagrees with the chain, not that a number was chosen badly. The same fixtures and expectations
// exist in every SDK, so a divergence between SDKs shows up as a failure in one of them.
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
		gasTokenId = 1,
		dataTokenId = 2,
		minimumGasOffer = 10,
		dataEscrowPerRow = 2,
		gasFeeTransfer = 10,
		gasFeeQuery = 10,
		gasFeeCreateTokenBase = 10_000_000_000,
		gasFeeCreateTokenSymbol = 10_000_000_000,
		gasFeeCreateTokenSeries = 2_500_000_000,
		gasFeePerByte = 250_000,
		gasFeeRegisterName = 10_000_000_000_000,
		gasBurnRatioMul = 1
	};

	// The mainnet / testnet gas-model-v2 configuration (special resolutions #79 and #68 carry the
	// same 29 fields).
	private static GasConfig V2Config()
	{
		var config = V1Config();
		config.version = 1;
		config.maxNameLength = 255;
		config.maxTokenSymbolLength = 255;
		config.dataEscrowPerRow = 200_000;
		config.legacyDataEscrowPerRow = 2;
		config.minimumGasBill = 10_000_000;
		config.policyFeeCreateTokenBase = 100_000_000_000_000;
		config.policyFeeCreateTokenSymbol = 100_000_000_000_000;
		config.policyFeeCreateTokenSeries = 25_000_000_000_000;
		config.policyFeeRegisterName = 100_000_000_000_000_000;
		return config;
	}

	// The localnet configuration of the live runs: policy fees at the 10 KCAL scale, escrow 50,000
	// atoms per row, everything else as mainnet.
	private static GasConfig LocalnetConfig()
	{
		var config = V2Config();
		config.dataEscrowPerRow = 50_000;
		config.policyFeeCreateTokenBase = 100_000_000_000;
		config.policyFeeCreateTokenSymbol = 100_000_000_000;
		config.policyFeeCreateTokenSeries = 25_000_000_000;
		config.policyFeeRegisterName = 1_000_000_000_000;
		return config;
	}

	private static NativeFeeEstimate Estimate(NativeFeeKind kind, GasConfig config, NativeFeeParams p) =>
		NativeFeeEstimator.EstimateNativeFee(kind, config, p);

	// A native SOUL or KCAL transfer is 170 signed bytes and escrows nothing, even into an address
	// that has never held the token, because the gas and data token balance rows are free.
	[Fact]
	public void Bills_a_native_gas_or_data_token_transfer_for_its_envelope_alone()
	{
		foreach (var tokenId in new ulong[] { 1, 2 })
		{
			var estimate = Estimate(NativeFeeKind.TransferFungible, V2Config(), new NativeFeeParams { EnvelopeBytes = 170, TokenId = tokenId });
			estimate.ExpectedGasBill.ShouldBe(42_600_000UL);
			estimate.MaxGas.ShouldBe(42_600_000UL);
			estimate.MaxData.ShouldBe(0UL);
			estimate.NewStorageQuanta.ShouldBe(0u);
		}
	}

	// The same envelope moving an ordinary token into a fresh holder creates one paid balance row,
	// which the chain adds to the byte count and escrows at the row price.
	[Fact]
	public void Bills_and_escrows_the_fresh_balance_row_of_an_ordinary_token_transfer()
	{
		var estimate = Estimate(NativeFeeKind.TransferFungible, V2Config(), new NativeFeeParams { EnvelopeBytes = 170, TokenId = 97 });
		estimate.ExpectedGasBill.ShouldBe(42_850_000UL);
		estimate.MaxData.ShouldBe(200_000UL);

		var held = Estimate(NativeFeeKind.TransferFungible, V2Config(), new NativeFeeParams { EnvelopeBytes = 170, TokenId = 97, RecipientHoldsToken = true });
		held.ExpectedGasBill.ShouldBe(42_600_000UL);
		held.MaxData.ShouldBe(0UL);
	}

	// Mainnet tx 6C1A6412... (block 9,075,820): a PoltergeistLite transfer of 178 bytes was billed
	// 44,600,000 - the figure the node printed in its "gas fees" abort.
	[Fact]
	public void Reproduces_the_first_live_mainnet_bill()
	{
		Estimate(NativeFeeKind.TransferFungible, V2Config(), new NativeFeeParams { EnvelopeBytes = 178, TokenId = 1 })
			.ExpectedGasBill.ShouldBe(44_600_000UL);
	}

	// A native MintFungible of 171 bytes into a fresh holder of a token whose supply row is in
	// place. The call returns the new balance, and a call result is block data exactly like the
	// envelope. The result's size depends on the balance it reports: 9 bytes while it fits int64, up
	// to 33 beyond - so stating BigFungible = false prices the 9-byte result exactly, and leaving it
	// unstated prices the 33-byte maximum, 24 bytes of block data more.
	[Fact]
	public void Bills_a_fungible_mint_with_its_9_byte_result_when_the_token_is_declared_small()
	{
		var estimate = Estimate(NativeFeeKind.MintFungible, V2Config(),
			new NativeFeeParams { EnvelopeBytes = 171, TokenId = 97, BigFungible = false, SupplyRowExists = true });
		estimate.ExpectedGasBill.ShouldBe(45_350_000UL);
		estimate.MaxData.ShouldBe(200_000UL);

		var defaulted = Estimate(NativeFeeKind.MintFungible, V2Config(),
			new NativeFeeParams { EnvelopeBytes = 171, TokenId = 97, SupplyRowExists = true });
		(defaulted.ExpectedGasBill - estimate.ExpectedGasBill).ShouldBe(24UL * 25 * 10_000);
	}

	// An NFT transfer of 170 bytes into a fresh holder. The owner's lookup row is deleted and the
	// recipient's created (net zero), the fresh balance row is the one net quantum; both new rows are
	// escrowed, the deleted row's deposit comes back at its own price.
	[Fact]
	public void Bills_an_NFT_transfer_for_its_net_rows_and_escrows_its_new_ones()
	{
		var estimate = Estimate(NativeFeeKind.TransferNonFungible, V2Config(), new NativeFeeParams { EnvelopeBytes = 170, TokenId = 7 });
		estimate.ExpectedGasBill.ShouldBe(42_850_000UL);
		estimate.NewStorageQuanta.ShouldBe(2u);
		estimate.DeletedStorageQuanta.ShouldBe(1u);
		estimate.MaxData.ShouldBe(400_000UL);
	}

	// The same transfer into an NFT-derived address (an infusion) pays one query fee more for the
	// owner check of the target.
	[Fact]
	public void Adds_the_query_fee_of_an_infusion_target()
	{
		Estimate(NativeFeeKind.TransferNonFungible, V2Config(), new NativeFeeParams { EnvelopeBytes = 170, TokenId = 7, ToIsNftAddress = true })
			.ExpectedGasBill.ShouldBe(42_950_000UL);

		// Minting into an NFT address pays the same owner lookup, once per call, on all three mint
		// kinds - the chain checks the recipient the same way it does for a transfer.
		foreach (var kind in new[] { NativeFeeKind.MintFungible, NativeFeeKind.MintNonFungible, NativeFeeKind.MintPhantasmaNonFungible })
		{
			var plain = Estimate(kind, V2Config(), new NativeFeeParams { EnvelopeBytes = 200, TokenId = 7, RomBytes = 10 });
			var infused = Estimate(kind, V2Config(), new NativeFeeParams { EnvelopeBytes = 200, TokenId = 7, RomBytes = 10, ToIsNftAddress = true });
			(infused.ExpectedGasBill - plain.ExpectedGasBill).ShouldBe(100_000UL);
		}
	}

	// A native NFT burn of 138 bytes; the mint's ten quanta are deleted and refunded, no net block
	// data, the token had been burned before and its supply row was in place.
	[Fact]
	public void Bills_an_NFT_burn_for_its_work_and_envelope_only()
	{
		var estimate = Estimate(NativeFeeKind.BurnNonFungible, V2Config(), new NativeFeeParams
		{
			EnvelopeBytes = 138,
			TokenId = 7,
			RomBytes = 3067 * 2 + 36,
			RomHasMetaId = true,
			TokenBurnedBefore = true,
			SupplyRowExists = true
		});
		estimate.ExpectedGasBill.ShouldBe(34_800_000UL);
		estimate.DeletedStorageQuanta.ShouldBe(10u);
		estimate.NewStorageQuanta.ShouldBe(0u);
		estimate.MaxData.ShouldBe(0UL);
	}

	// Two settled CreateToken bills with 7-character symbols. The fungible one is 374 signed bytes
	// and writes the symbol, token-info and supply rows and returns the u64 token id; the NFT-capable
	// one (466 bytes) adds the series counter. Policy fee 10 KCAL + 10 KCAL >> 6.
	[Fact]
	public void Reproduces_the_two_localnet_CreateToken_bills_to_the_atom()
	{
		var fungible = Estimate(NativeFeeKind.CreateToken, LocalnetConfig(), new NativeFeeParams
		{
			EnvelopeBytes = 374,
			SymbolLength = 7,
			// The Call arguments ARE the serialized TokenInfo, so their length is the envelope minus
			// the 100-byte transaction header and the 58 bytes the Call header and witness array add.
			TokenInfoBytes = 374 - 100 - 58
		});
		fungible.ExpectedGasBill.ShouldBe(101_658_750_000UL);
		fungible.NewStorageQuanta.ShouldBe(3u);
		fungible.MaxData.ShouldBe(150_000UL);

		var nft = Estimate(NativeFeeKind.CreateToken, LocalnetConfig(), new NativeFeeParams
		{
			EnvelopeBytes = 466,
			SymbolLength = 7,
			TokenInfoBytes = 466 - 100 - 58,
			NonFungible = true
		});
		nft.ExpectedGasBill.ShouldBe(101_682_000_000UL);
		nft.NewStorageQuanta.ShouldBe(4u);
		nft.MaxData.ShouldBe(200_000UL);
	}

	[Fact]
	public void Reproduces_the_localnet_CreateTokenSeries_bill()
	{
		var estimate = Estimate(NativeFeeKind.CreateTokenSeries, LocalnetConfig(), new NativeFeeParams
		{
			EnvelopeBytes = 246,
			// As above, less the 8-byte token id that precedes the SeriesInfo in the Call arguments.
			SeriesInfoBytes = 246 - 100 - 58 - 8,
			SeriesHasMetaId = true
		});
		estimate.ExpectedGasBill.ShouldBe(25_063_250_000UL);
		estimate.NewStorageQuanta.ShouldBe(3u);
	}

	// Two settled deterministic Phantasma mints. The 182-byte public ROM was the token's first mint
	// to that owner (fresh balance row, 5 quanta); the 3,083-byte one found the balance row in place
	// and its canonical ROM alone took seven quanta (10 in total). Each instance pays the mint plus
	// two query fees and returns 40 bytes after the 4-byte count.
	//
	// Both were minted into a UNIQUE series of a token whose supply row was in place, and every
	// settled-bill case below says both explicitly: the series mode and the supply row are chain
	// state the message does not carry, so the calculator assumes the costlier reading of each when
	// nobody tells it. Leaving either out here would compare the chain's receipt against a
	// deliberate over-estimate.
	[Fact]
	public void Reproduces_the_two_localnet_Phantasma_NFT_mint_bills()
	{
		var small = Estimate(NativeFeeKind.MintPhantasmaNonFungible, LocalnetConfig(), new NativeFeeParams
		{
			EnvelopeBytes = 413,
			TokenId = 9,
			RomBytes = 182,
			DuplicatedSeries = false,
			SupplyRowExists = true
		});
		small.ExpectedGasBill.ShouldBe(115_800_000UL);
		small.NewStorageQuanta.ShouldBe(5u);
		small.MaxData.ShouldBe(250_000UL);

		var large = Estimate(NativeFeeKind.MintPhantasmaNonFungible, LocalnetConfig(), new NativeFeeParams
		{
			EnvelopeBytes = 3314,
			TokenId = 9,
			RomBytes = 3083,
			RecipientHoldsToken = true,
			DuplicatedSeries = false,
			SupplyRowExists = true
		});
		large.ExpectedGasBill.ShouldBe(842_300_000UL);
		large.NewStorageQuanta.ShouldBe(10u);
		large.MaxData.ShouldBe(500_000UL);
	}

	// The same model at a different network's prices, including two 166-byte mints of the same size
	// where only the first paid for the owner's balance row.
	[Theory]
	[InlineData(398u, 167u, false, 112_050_000UL, 5u)]
	[InlineData(3298u, 3067u, true, 838_300_000UL, 10u)]
	[InlineData(397u, 166u, false, 111_800_000UL, 5u)]
	[InlineData(397u, 166u, true, 111_550_000UL, 4u)]
	public void Reproduces_the_four_testnet_Phantasma_NFT_mint_bills(uint envelopeBytes, uint romBytes, bool recipientHoldsToken, ulong bill, uint quanta)
	{
		var estimate = Estimate(NativeFeeKind.MintPhantasmaNonFungible, V2Config(), new NativeFeeParams
		{
			EnvelopeBytes = envelopeBytes,
			TokenId = 9,
			RomBytes = romBytes,
			RecipientHoldsToken = recipientHoldsToken,
			DuplicatedSeries = false,
			SupplyRowExists = true
		});
		estimate.ExpectedGasBill.ShouldBe(bill);
		estimate.NewStorageQuanta.ShouldBe(quanta);
	}

	// A sweep of settled mints across the quantum boundary (public ROM bytes -> quanta, the first
	// mint also paying for the balance row): 268 -> 5, 968 -> 5, 1168 -> 6, 3068 -> 10, 5068 -> 13.
	// 968 and 1168 straddle the 1024-byte boundary, which is where a wrong ROM model shows up.
	[Theory]
	[InlineData(268u, false, 5u)]
	[InlineData(968u, true, 5u)]
	[InlineData(1168u, true, 6u)]
	[InlineData(3068u, true, 10u)]
	[InlineData(5068u, true, 13u)]
	public void Reproduces_the_ROM_size_sweep_quanta(uint romBytes, bool recipientHoldsToken, uint quanta)
	{
		Estimate(NativeFeeKind.MintPhantasmaNonFungible, V2Config(), new NativeFeeParams
		{
			EnvelopeBytes = 1000,
			RomBytes = romBytes,
			RecipientHoldsToken = recipientHoldsToken,
			SupplyRowExists = true
		}).NewStorageQuanta.ShouldBe(quanta);
	}

	// Several instances in one transaction: the work units, the per-instance query fees, the call
	// result bytes and the per-instance storage all scale with the count, while the recipient's
	// balance row is paid for exactly once. A settled three-instance Phantasma mint of 75-byte public
	// ROMs, 490 signed bytes: 13 quanta are one balance row plus, per instance, one ROM row and the
	// three fixed rows a deterministic mint writes.
	[Fact]
	public void Scales_a_multi_instance_Phantasma_mint_by_its_instance_count()
	{
		var estimate = Estimate(NativeFeeKind.MintPhantasmaNonFungible, LocalnetConfig(), new NativeFeeParams
		{
			EnvelopeBytes = 490,
			Count = 3,
			RomBytes = 75,
			DuplicatedSeries = false,
			SupplyRowExists = true
		});
		estimate.ExpectedGasBill.ShouldBe(157_650_000UL);
		estimate.NewStorageQuanta.ShouldBe(13u);
		estimate.MaxData.ShouldBe(650_000UL);

		// The recipient's balance row is paid for once however many instances are minted, so one
		// instance writes four of the thirteen quanta plus that shared row, not a third of them.
		Estimate(NativeFeeKind.MintPhantasmaNonFungible, LocalnetConfig(), new NativeFeeParams
		{
			EnvelopeBytes = 490,
			Count = 1,
			RomBytes = 75,
			DuplicatedSeries = false,
			SupplyRowExists = true
		}).NewStorageQuanta.ShouldBe(5u);
	}

	// A duplicated series is read twice per instance rather than once - the mode check and the
	// shared ROM each read the token info - and its supply is read once per series for the whole
	// transaction, because the chain reuses the number it already read. The counts therefore scale
	// differently and the model has to keep them apart.
	[Fact]
	public void Charges_a_duplicated_series_three_queries_an_instance_and_one_for_the_series()
	{
		NativeFeeParams Shared(uint distinctSeries) => new()
		{
			EnvelopeBytes = 490,
			Count = 3,
			RomBytes = 75,
			DuplicatedSeries = true,
			DistinctSeriesCount = distinctSeries
		};
		var oneSeries = Estimate(NativeFeeKind.MintPhantasmaNonFungible, LocalnetConfig(), Shared(1));
		var unique = Estimate(NativeFeeKind.MintPhantasmaNonFungible, LocalnetConfig(), new NativeFeeParams
		{
			EnvelopeBytes = 490,
			Count = 3,
			RomBytes = 75,
			DuplicatedSeries = false
		});
		// Three extra instance queries and one supply query over the unique-series bill.
		(oneSeries.ExpectedGasBill - unique.ExpectedGasBill).ShouldBe(40UL * 10_000);

		var threeSeries = Estimate(NativeFeeKind.MintPhantasmaNonFungible, LocalnetConfig(), Shared(3));
		(threeSeries.ExpectedGasBill - oneSeries.ExpectedGasBill).ShouldBe(20UL * 10_000);

		// A series count nobody could have minted is a bookkeeping error, not a price.
		Should.Throw<ArgumentOutOfRangeException>(() => Estimate(NativeFeeKind.MintPhantasmaNonFungible, LocalnetConfig(), Shared(4)))
			.Message.ShouldContain("DistinctSeriesCount");
	}

	// A settled two-instance NFT transfer of 182 signed bytes: each instance deletes the owner's
	// lookup row and creates the recipient's, so only the recipient's new balance row is billed,
	// while the escrow ceiling still covers all three rows the transaction may create.
	[Fact]
	public void Scales_a_multi_instance_NFT_transfer_by_its_instance_count()
	{
		var estimate = Estimate(NativeFeeKind.TransferNonFungible, LocalnetConfig(), new NativeFeeParams { EnvelopeBytes = 182, Count = 2, TokenId = 181 });
		estimate.ExpectedGasBill.ShouldBe(45_950_000UL);
		estimate.NewStorageQuanta.ShouldBe(3u);
		estimate.DeletedStorageQuanta.ShouldBe(2u);
		estimate.MaxData.ShouldBe(150_000UL);
	}

	// A settled RegisterName of a 10-character name, 213 signed bytes. Governance rows are free data,
	// so the bill is the policy fee (10,000,000 KCAL >> 9) plus the envelope and nothing else.
	[Fact]
	public void Reproduces_the_testnet_RegisterName_bill()
	{
		var estimate = Estimate(NativeFeeKind.RegisterName, V2Config(), new NativeFeeParams { EnvelopeBytes = 213, NameLength = 10 });
		estimate.ExpectedGasBill.ShouldBe(195_312_553_250_000UL);
		estimate.MaxData.ShouldBe(0UL);
	}

	// Burning an NFT returns whatever its own address holds, and the chain charges for each returned
	// asset as the transfers it performs: a transfer fee plus the owner lookup of the NFT-address
	// source per fungible token; an instance query, a transfer per instance and that lookup per NFT
	// token. The returned rows never add block data - a burn refunds more than the returns create -
	// so the bill moves by the work alone, while the escrow ceiling covers a balance row the burner
	// lacks and the moved lookup rows. Measured live 2026-09-02: +200,000 for one infused KCAL atom,
	// +700,000 for KCAL, a custom token and an NFT together.
	[Fact]
	public void Prices_the_assets_a_burned_NFT_returns()
	{
		NativeFeeParams Burn(params InfusedAsset[] infusions) => new()
		{
			EnvelopeBytes = 138,
			TokenId = 7,
			RomBytes = 3067 * 2 + 36,
			RomHasMetaId = true,
			TokenBurnedBefore = true,
			SupplyRowExists = true,
			Infusions = infusions
		};
		var empty = Estimate(NativeFeeKind.BurnNonFungible, V2Config(), Burn());
		empty.ExpectedGasBill.ShouldBe(34_800_000UL);

		// One fungible token: a transfer and a query. The gas token's rows are free, so nothing else.
		var kcal = Estimate(NativeFeeKind.BurnNonFungible, V2Config(), Burn(new InfusedAsset { TokenId = 1 }));
		(kcal.ExpectedGasBill - empty.ExpectedGasBill).ShouldBe(20UL * 10_000);
		kcal.NewStorageQuanta.ShouldBe(empty.NewStorageQuanta);
		kcal.DeletedStorageQuanta.ShouldBe(empty.DeletedStorageQuanta);

		// A custom token the burner does not hold: the same work, plus the balance row the return
		// creates - and the NFT address's own row, which the return deletes.
		var custom = Estimate(NativeFeeKind.BurnNonFungible, V2Config(), Burn(new InfusedAsset { TokenId = 97 }));
		(custom.ExpectedGasBill - empty.ExpectedGasBill).ShouldBe(20UL * 10_000);
		custom.NewStorageQuanta.ShouldBe(empty.NewStorageQuanta + 1);
		custom.DeletedStorageQuanta.ShouldBe(empty.DeletedStorageQuanta + 1);
		custom.MaxData.ShouldBe(empty.MaxData + V2Config().dataEscrowPerRow);
		Estimate(NativeFeeKind.BurnNonFungible, V2Config(), Burn(new InfusedAsset { TokenId = 97, BurnerHoldsToken = true }))
			.NewStorageQuanta.ShouldBe(empty.NewStorageQuanta);
		// An id the reader could not resolve is priced as a paid row: over-covering, never short.
		Estimate(NativeFeeKind.BurnNonFungible, V2Config(), Burn(new InfusedAsset()))
			.NewStorageQuanta.ShouldBe(empty.NewStorageQuanta + 1);

		// Two instances of an NFT token: the instance query, two transfers, the lookup; a balance row
		// plus two moved lookup rows created, the NFT address's balance row and two lookups deleted.
		var nft = Estimate(NativeFeeKind.BurnNonFungible, V2Config(), Burn(new InfusedAsset { TokenId = 9, NonFungible = true, InstanceCount = 2 }));
		(nft.ExpectedGasBill - empty.ExpectedGasBill).ShouldBe(40UL * 10_000);
		nft.NewStorageQuanta.ShouldBe(empty.NewStorageQuanta + 3);
		nft.DeletedStorageQuanta.ShouldBe(empty.DeletedStorageQuanta + 3);

		// The live combination: KCAL, a held custom token and one NFT - seventy units.
		var all = Estimate(NativeFeeKind.BurnNonFungible, V2Config(), Burn(
			new InfusedAsset { TokenId = 1 },
			new InfusedAsset { TokenId = 97, BurnerHoldsToken = true },
			new InfusedAsset { TokenId = 9, NonFungible = true, InstanceCount = 1, BurnerHoldsToken = true }));
		(all.ExpectedGasBill - empty.ExpectedGasBill).ShouldBe(70UL * 10_000);

		Should.Throw<ArgumentOutOfRangeException>(() => Estimate(NativeFeeKind.BurnNonFungible, V2Config(), Burn(new InfusedAsset { TokenId = 9, NonFungible = true, InstanceCount = 0 })))
			.Message.ShouldContain("InstanceCount");
	}

	// A tiny v2 tx can never bill below the consensus floor; the offer must also respect the
	// admission check maxGas >= minimumGasBill.
	[Fact]
	public void Applies_the_minimum_bill_floor()
	{
		var config = V2Config();
		config.minimumGasBill = 10_000_000_000;
		var estimate = Estimate(NativeFeeKind.TransferFungible, config, new NativeFeeParams { EnvelopeBytes = 170, TokenId = 1 });
		estimate.ExpectedGasBill.ShouldBe(10_000_000_000UL);
		estimate.MaxGas.ShouldBe(10_000_000_000UL);
	}

	// Facts about chain state that the message cannot carry are defaulted to the case that COSTS
	// MORE, because the offer is spent against the real bill and a short one aborts the transaction
	// while an over-offer is refunded. These pin that direction; each case fails if a default flips.
	[Fact]
	public void Assumes_a_Phantasma_series_is_duplicated_until_told_otherwise()
	{
		NativeFeeParams Shared(bool? duplicated) => new() { EnvelopeBytes = 490, Count = 3, RomBytes = 75, DuplicatedSeries = duplicated };
		var assumed = Estimate(NativeFeeKind.MintPhantasmaNonFungible, LocalnetConfig(), Shared(null));
		var duplicated = Estimate(NativeFeeKind.MintPhantasmaNonFungible, LocalnetConfig(), Shared(true));
		var unique = Estimate(NativeFeeKind.MintPhantasmaNonFungible, LocalnetConfig(), Shared(false));
		// Three extra instance queries plus one supply query: 40 units at this config's multiplier.
		(duplicated.ExpectedGasBill - unique.ExpectedGasBill).ShouldBe(40UL * 10_000);
		assumed.ExpectedGasBill.ShouldBe(duplicated.ExpectedGasBill);
	}

	[Fact]
	public void Assumes_a_minted_ROM_carries_a_meta_id_which_is_a_row_it_must_escrow_for()
	{
		NativeFeeParams Shared(bool? metaId) => new() { EnvelopeBytes = 300, TokenId = 97, RomBytes = 64, RomHasMetaId = metaId };
		var assumed = Estimate(NativeFeeKind.MintNonFungible, V2Config(), Shared(null));
		var withMetaId = Estimate(NativeFeeKind.MintNonFungible, V2Config(), Shared(true));
		var without = Estimate(NativeFeeKind.MintNonFungible, V2Config(), Shared(false));
		withMetaId.NewStorageQuanta.ShouldBe(without.NewStorageQuanta + 1);
		assumed.NewStorageQuanta.ShouldBe(withMetaId.NewStorageQuanta);
		// MaxData is the one that has to be right: the chain aborts a transaction whose escrow
		// exceeds it, so a row assumed away is not an under-offer but a failed transaction.
		assumed.MaxData.ShouldBe(withMetaId.MaxData);
		(assumed.MaxData - without.MaxData).ShouldBe(V2Config().dataEscrowPerRow);
	}

	// Same flag, same default on a burn - it keeps the deleted rows mirroring what the mint wrote -
	// but there it moves a reported number and nothing else. Deleted rows are refunded, MaxData
	// covers only the rows an operation CREATES, and a burn always deletes more than it creates, so
	// the block-data term floors at zero whichever way the flag goes. This pins both halves: the
	// count follows the flag, the price does not.
	[Fact]
	public void Counts_a_burned_meta_id_row_but_does_not_price_on_it()
	{
		NativeFeeParams Shared(bool? metaId) => new() { EnvelopeBytes = 300, TokenId = 97, RomBytes = 64, RomHasMetaId = metaId };
		var assumed = Estimate(NativeFeeKind.BurnNonFungible, V2Config(), Shared(null));
		var without = Estimate(NativeFeeKind.BurnNonFungible, V2Config(), Shared(false));
		assumed.DeletedStorageQuanta.ShouldBe(without.DeletedStorageQuanta + 1);
		assumed.ExpectedGasBill.ShouldBe(without.ExpectedGasBill);
		assumed.MaxGas.ShouldBe(without.MaxGas);
		assumed.MaxData.ShouldBe(without.MaxData);
	}

	[Fact]
	public void Assumes_a_created_series_carries_a_meta_id_which_is_one_more_row()
	{
		var assumed = Estimate(NativeFeeKind.CreateTokenSeries, V2Config(), new NativeFeeParams { EnvelopeBytes = 300, SeriesInfoBytes = 100 });
		var without = Estimate(NativeFeeKind.CreateTokenSeries, V2Config(), new NativeFeeParams { EnvelopeBytes = 300, SeriesInfoBytes = 100, SeriesHasMetaId = false });
		assumed.NewStorageQuanta.ShouldBe(without.NewStorageQuanta + 1);
	}

	// The supply row disappears when its balance reaches exactly zero - a limited token fully in
	// circulation, an unlimited one with nothing outstanding - and the next mint or burn recreates
	// it. Unstated, every mint and burn prices that recreation: one quantum in the bill and the
	// escrow ceiling. Transfers never touch the row, and the chain's own gas and data tokens are
	// free rows, so neither moves with the flag.
	[Fact]
	public void Assumes_the_supply_row_must_be_recreated_on_mints_and_burns()
	{
		foreach (var kind in new[]
		{
			NativeFeeKind.MintFungible,
			NativeFeeKind.BurnFungible,
			NativeFeeKind.MintNonFungible,
			NativeFeeKind.MintPhantasmaNonFungible,
			NativeFeeKind.BurnNonFungible
		})
		{
			var assumed = Estimate(kind, V2Config(), new NativeFeeParams { EnvelopeBytes = 300, TokenId = 97, RomBytes = 64 });
			var inPlace = Estimate(kind, V2Config(), new NativeFeeParams { EnvelopeBytes = 300, TokenId = 97, RomBytes = 64, SupplyRowExists = true });
			assumed.NewStorageQuanta.ShouldBe(inPlace.NewStorageQuanta + 1);
			(assumed.MaxData - inPlace.MaxData).ShouldBe(V2Config().dataEscrowPerRow);
			// An NFT burn deletes more quanta than it creates, so its block-data term floors at zero
			// either way and only the escrow ceiling moves; everywhere else the bill moves too.
			(assumed.ExpectedGasBill - inPlace.ExpectedGasBill).ShouldBe(kind == NativeFeeKind.BurnNonFungible ? 0UL : 25UL * 10_000);
		}

		var transfer = Estimate(NativeFeeKind.TransferFungible, V2Config(), new NativeFeeParams { EnvelopeBytes = 300, TokenId = 97 });
		var transferStated = Estimate(NativeFeeKind.TransferFungible, V2Config(), new NativeFeeParams { EnvelopeBytes = 300, TokenId = 97, SupplyRowExists = true });
		transfer.ShouldBe(transferStated);
		var gasToken = Estimate(NativeFeeKind.BurnFungible, V2Config(), new NativeFeeParams { EnvelopeBytes = 300, TokenId = 1 });
		var gasTokenStated = Estimate(NativeFeeKind.BurnFungible, V2Config(), new NativeFeeParams { EnvelopeBytes = 300, TokenId = 1, SupplyRowExists = true });
		gasToken.ShouldBe(gasTokenStated);
	}

	[Fact]
	public void Requires_the_envelope_size_under_gas_model_v2()
	{
		Should.Throw<ArgumentException>(() => NativeFeeEstimator.EstimateNativeFee(NativeFeeKind.TransferFungible, V2Config()))
			.Message.ShouldContain("EnvelopeBytes is required");
	}

	// v1 transfer with an existing recipient row: bill is the pure work term 10 * 10000.
	[Fact]
	public void V1_bills_work_only_for_a_transfer_to_an_existing_recipient()
	{
		var estimate = Estimate(NativeFeeKind.TransferFungible, V1Config(), new NativeFeeParams { TokenId = 97, RecipientHoldsToken = true });
		estimate.ExpectedGasBill.ShouldBe(100_000UL);
		// stdFee shape: 2x min offer + work + flat 1 KiB byte allowance.
		estimate.MaxGas.ShouldBe(10UL * 2 + 100_000 + 1024 * 250_000);
		estimate.MaxData.ShouldBe(0UL);
	}

	// v1 transfer default (one fresh row): the row quantum joins the byte fee and the escrow shows
	// up in MaxData at the v1 price.
	[Fact]
	public void V1_includes_one_fresh_row_by_default()
	{
		var estimate = Estimate(NativeFeeKind.TransferFungible, V1Config(), new NativeFeeParams { TokenId = 97 });
		estimate.ExpectedGasBill.ShouldBe(100_000UL + 1 * 250_000);
		estimate.MaxData.ShouldBe(2UL);
	}

	// v1 CreateToken: the product prices ride the work term through the multiplier.
	[Fact]
	public void V1_prices_token_creation_through_the_multiplier()
	{
		var estimate = Estimate(NativeFeeKind.CreateToken, V1Config(), new NativeFeeParams { SymbolLength = 5, TokenInfoBytes = 200 });
		// (10_000_000_000 + 10_000_000_000 >> 4) * 10000 + 3 rows + 8 result bytes at the v1 byte fee.
		estimate.ExpectedGasBill.ShouldBe((10_000_000_000UL + (10_000_000_000UL >> 4)) * 10_000 + 11 * 250_000);
	}

	// The name price halves per character after the first under both models.
	[Fact]
	public void Halves_the_name_price_per_character_under_both_models()
	{
		var v1 = Estimate(NativeFeeKind.RegisterName, V1Config(), new NativeFeeParams { NameLength = 5 });
		v1.ExpectedGasBill.ShouldBe((10_000_000_000_000UL >> 4) * 10_000);
		var v2 = Estimate(NativeFeeKind.RegisterName, V2Config(), new NativeFeeParams { EnvelopeBytes = 200, NameLength = 5 });
		v2.ExpectedGasBill.ShouldBe((100_000_000_000_000_000UL >> 4) + 200 * 25 * 10_000);
	}

	[Fact]
	public void Zeroes_scaled_terms_on_an_oversized_feeShift()
	{
		var config = V2Config();
		config.feeShift = 64;
		// Nothing but the policy fee and the floor can remain when the scaled terms vanish.
		Estimate(NativeFeeKind.TransferFungible, config, new NativeFeeParams { EnvelopeBytes = 170, TokenId = 1 })
			.ExpectedGasBill.ShouldBe(config.minimumGasBill);
	}

	// A VM script has no closed-form bill; the estimate is a budget of work units, event bytes and
	// storage quanta that a caller sizes, not a prediction.
	[Fact]
	public void Budgets_a_VM_allowance_for_scripts()
	{
		var estimate = Estimate(NativeFeeKind.Script, V2Config(), new NativeFeeParams { EnvelopeBytes = 300 });
		estimate.ExpectedGasBill.ShouldBe((5000UL + (300 + 4 + 512) * 25) * 10_000);
		estimate.NewStorageQuanta.ShouldBe(4u);
		var tighter = Estimate(NativeFeeKind.Script, V2Config(), new NativeFeeParams { EnvelopeBytes = 300, ScriptUnitsAllowance = 1000, ScriptEventBytes = 0, ScriptStorageQuanta = 0 });
		tighter.ExpectedGasBill.ShouldBe((1000UL + 300 * 25) * 10_000);
	}

	[Fact]
	public void Follows_the_witness_layout_for_envelope_sizes()
	{
		NativeFeeEstimator.EnvelopeBytesFor(NativeFeeKind.TransferFungible, 106, 1).ShouldBe(170u);
		NativeFeeEstimator.EnvelopeBytesFor(NativeFeeKind.TransferFungible, 138, 2).ShouldBe(266u);
		NativeFeeEstimator.EnvelopeBytesFor(NativeFeeKind.CreateToken, 200, 1).ShouldBe(300u);
		NativeFeeEstimator.EnvelopeBytesFor(NativeFeeKind.Script, 200, 2).ShouldBe(396u);
	}

	[Fact]
	public void Measures_rows_in_1024_byte_quanta_and_the_canonical_ROM_at_twice_the_public_one()
	{
		NativeFeeEstimator.StorageQuantaFor(0).ShouldBe(0u);
		NativeFeeEstimator.StorageQuantaFor(1).ShouldBe(1u);
		NativeFeeEstimator.StorageQuantaFor(1024).ShouldBe(1u);
		NativeFeeEstimator.StorageQuantaFor(1025).ShouldBe(2u);
		NativeFeeEstimator.PhantasmaCanonicalRomBytes(182).ShouldBe(400u);
	}

	// The chain's length-halved policy fee is defined up to 64 characters; longer names and symbols
	// have no offline price, so the calculator refuses instead of quoting one. A length the chain
	// itself could never admit is refused on that ground first.
	[Fact]
	public void Refuses_to_price_a_name_or_symbol_past_the_length_the_chain_can_shift()
	{
		var config = V2Config();
		Estimate(NativeFeeKind.RegisterName, config, new NativeFeeParams { EnvelopeBytes = 200, NameLength = 64 }).ExpectedGasBill.ShouldBe(200UL * 25 * 10_000);
		Should.Throw<ArgumentOutOfRangeException>(() => Estimate(NativeFeeKind.RegisterName, config, new NativeFeeParams { EnvelopeBytes = 200, NameLength = 65 }))
			.Message.ShouldContain("cannot be priced offline");
		Should.Throw<ArgumentOutOfRangeException>(() => Estimate(NativeFeeKind.CreateToken, config, new NativeFeeParams { EnvelopeBytes = 200, SymbolLength = 65 }))
			.Message.ShouldContain("cannot be priced offline");

		var tight = V2Config();
		tight.maxNameLength = 10;
		Should.Throw<ArgumentOutOfRangeException>(() => Estimate(NativeFeeKind.RegisterName, tight, new NativeFeeParams { EnvelopeBytes = 200, NameLength = 11 }))
			.Message.ShouldContain("exceeds the chain maximum");
	}

	[Fact]
	public void Rejects_invalid_inputs()
	{
		Should.Throw<ArgumentOutOfRangeException>(() => Estimate(NativeFeeKind.TransferNonFungible, V2Config(), new NativeFeeParams { EnvelopeBytes = 170, Count = 0 }));
		Should.Throw<ArgumentException>(() => Estimate(NativeFeeKind.RegisterName, V2Config(), new NativeFeeParams { EnvelopeBytes = 170 }))
			.Message.ShouldContain("NameLength is required");
		Should.Throw<ArgumentException>(() => Estimate(NativeFeeKind.MintNonFungible, V2Config(), new NativeFeeParams { EnvelopeBytes = 170, Count = 2, RomBytesPerInstance = new uint[] { 10 } }))
			.Message.ShouldContain("one entry per instance");
		Should.Throw<ArgumentOutOfRangeException>(() => NativeFeeEstimator.EnvelopeBytesFor(NativeFeeKind.TransferFungible, -1, 1));
	}
}

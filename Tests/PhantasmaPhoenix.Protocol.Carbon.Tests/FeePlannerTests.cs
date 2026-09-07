using System.Numerics;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

// The planner reads the operation and every message-carried fact out of the message, sizes the
// envelope without a key, and hands the calculator the state facts the caller stated. The calls are
// assembled directly rather than through the builders, so a failure here names the planner.
public class FeePlannerTests
{
	// The mainnet gas-model-v2 configuration (special resolution #79).
	private static GasConfig MainnetConfig() => new()
	{
		version = 1,
		maxNameLength = 255,
		maxTokenSymbolLength = 255,
		feeShift = 0,
		maxStructureSize = 1_048_576,
		feeMultiplier = 10_000,
		gasTokenId = 1,
		dataTokenId = 2,
		minimumGasOffer = 10,
		dataEscrowPerRow = 200_000,
		gasFeeTransfer = 10,
		gasFeeQuery = 10,
		gasFeeCreateTokenBase = 10_000_000_000,
		gasFeeCreateTokenSymbol = 10_000_000_000,
		gasFeeCreateTokenSeries = 2_500_000_000,
		gasFeePerByte = 250_000,
		gasFeeRegisterName = 10_000_000_000_000,
		gasBurnRatioMul = 1,
		gasBurnRatioShift = 0,
		minimumGasBill = 10_000_000,
		policyFeeCreateTokenBase = 100_000_000_000_000,
		policyFeeCreateTokenSymbol = 100_000_000_000_000,
		policyFeeCreateTokenSeries = 25_000_000_000_000,
		policyFeeRegisterName = 100_000_000_000_000_000,
		legacyDataEscrowPerRow = 2
	};

	private static readonly PhantasmaKeys Payer = PhantasmaKeys.FromWIF("KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d");
	private static readonly PhantasmaKeys Owner = PhantasmaKeys.FromWIF("KwVG94yjfVg1YKFyRxAGtug93wdRbmLnqqrFV6Yd2CiA9KZDAp4H");
	private static readonly Bytes32 PayerPub = new(Payer.PublicKey);
	private static readonly Bytes32 OwnerPub = new(Owner.PublicKey);
	private static readonly GasConfig Config = MainnetConfig();
	private static readonly FeePlanOptions OneWitness = new() { WitnessCount = 1 };

	// bill = (work + 25 * (envelope + net quanta + result)) * 10000, plus the policy fee.
	private static ulong Bill(ulong work, int blockData, ulong policy = 0) => (work + 25UL * (ulong)blockData) * 10_000 + policy;

	private static TxMsg Msg(TxTypes type, object inner) => new()
	{
		type = type,
		expiry = 1_787_000_000_000,
		maxGas = 0,
		maxData = 0,
		gasFrom = PayerPub,
		payload = SmallString.Empty,
		msg = inner
	};

	private static TxMsg Transfer(ulong tokenId, Bytes32? to = null) =>
		Msg(TxTypes.TransferFungible, new TxMsgTransferFungible { to = to ?? OwnerPub, tokenId = tokenId, amount = 10_000_000 });

	private static TxMsg TokenCall(TokenContract_Methods method, byte[] args) =>
		Msg(TxTypes.Call, new TxMsgCall { moduleId = (uint)ModuleId.Token, methodId = (uint)method, args = args });

	private static TokenInfo TokenInfo(bool nonFungible, byte[]? metadata = null) =>
		TokenInfoBuilder.Build(
			"GPX",
			new IntX(0),
			nonFungible,
			nonFungible ? 0u : 2u,
			PayerPub,
			metadata ?? TokenMetadataBuilder.BuildAndSerialize(new Dictionary<string, string>
			{
				["name"] = "Plan probe",
				["icon"] = "data:image/png;base64,iVBORw0KGgo=",
				["url"] = "https://example.invalid/p",
				["description"] = "x"
			}),
			nonFungible ? TokenSchemasBuilder.BuildAndSerialize(TokenSchemasBuilder.PrepareStandardTokenSchemas(false)) : null);

	private static PhantasmaNftMintInfo PhantasmaMint(BigInteger seriesId, int romBytes) => new()
	{
		phantasmaSeriesId = new IntX(seriesId),
		rom = new byte[romBytes],
		ram = Array.Empty<byte>()
	};

	private static TxMsg PhantasmaMintCall(PhantasmaNftMintInfo[] tokens, Bytes32? recipient = null) =>
		TokenCall(TokenContract_Methods.MintPhantasmaNonFungible, CarbonBlob.Serialize(new MintPhantasmaNonFungibleArgs
		{
			tokenId = 9,
			address = recipient ?? OwnerPub,
			tokens = tokens
		}));

	[Fact]
	public void Plans_a_gas_token_transfer_for_its_envelope_alone_and_applies_it_without_touching_the_input()
	{
		var msg = Transfer(Config.gasTokenId);
		var plan = FeePlanner.Plan(msg, Config);

		plan.Kinds.ShouldBe(new[] { NativeFeeKind.TransferFungible });
		plan.EnvelopeBytes.ShouldBe(170);
		plan.ExpectedGasBill.ShouldBe(42_600_000UL);
		plan.MaxGas.ShouldBe(42_600_000UL);
		plan.MaxData.ShouldBe(0UL);

		var planned = plan.Apply(msg);
		planned.maxGas.ShouldBe(42_600_000UL);
		msg.maxGas.ShouldBe(0UL);
		// The size the plan was priced for is the size the signed transaction really has.
		TxMsgSigner.SignAndSerializeWithKeys(planned, Payer).Length.ShouldBe(plan.EnvelopeBytes);
	}

	[Fact]
	public void Escrows_the_fresh_balance_row_of_an_ordinary_token_unless_the_recipient_holds_it()
	{
		var msg = Transfer(97);
		FeePlanner.Plan(msg, Config).MaxData.ShouldBe(200_000UL);
		FeePlanner.Plan(msg, Config).ExpectedGasBill.ShouldBe(Bill(10, 171));
		var held = FeePlanner.Plan(msg, Config, new FeePlanOptions { RecipientHoldsToken = true });
		held.MaxData.ShouldBe(0UL);
		held.ExpectedGasBill.ShouldBe(Bill(10, 170));
	}

	[Fact]
	public void Sizes_a_gas_payer_transfer_with_both_signatures()
	{
		var msg = Msg(TxTypes.TransferFungible_GasPayer, new TxMsgTransferFungible_GasPayer { to = PayerPub, from = OwnerPub, tokenId = Config.gasTokenId, amount = 5 });
		var plan = FeePlanner.Plan(msg, Config);
		plan.EnvelopeBytes.ShouldBe(106 + 32 + 128);
		plan.ExpectedGasBill.ShouldBe(Bill(10, plan.EnvelopeBytes));
		TxMsgSigner.SignAndSerializeWithKeys(plan.Apply(msg), Payer, Owner).Length.ShouldBe(plan.EnvelopeBytes);
	}

	// Every call is a witness-array type, so each plan states how many signatures the envelope will
	// carry; the planner refuses to assume a number that decides the size of the bill.
	[Fact]
	public void Refuses_to_guess_how_many_witnesses_a_call_will_carry()
	{
		var msg = TokenCall(TokenContract_Methods.CreateToken, CarbonBlob.Serialize(TokenInfo(false)));
		Should.Throw<ArgumentException>(() => FeePlanner.Plan(msg, Config)).Message.ShouldContain("WitnessCount");
	}

	[Fact]
	public void Reads_the_token_being_created_out_of_the_call()
	{
		var info = TokenInfo(false);
		var args = CarbonBlob.Serialize(info);
		var plan = FeePlanner.Plan(TokenCall(TokenContract_Methods.CreateToken, args), Config, OneWitness);

		plan.Kinds.ShouldBe(new[] { NativeFeeKind.CreateToken });
		// Symbol lookup + token info (one quantum at this size) + the supply row.
		plan.NewStorageQuanta.ShouldBe(3u);
		plan.MaxData.ShouldBe(600_000UL);
		var policy = 100_000_000_000_000UL + (100_000_000_000_000UL >> 2); // 3-char symbol
		plan.ExpectedGasBill.ShouldBe(Bill(0, plan.EnvelopeBytes + 3 + 8, policy));

		var nft = FeePlanner.Plan(TokenCall(TokenContract_Methods.CreateToken, CarbonBlob.Serialize(TokenInfo(true))), Config, OneWitness);
		// An NFT token adds the series counter row.
		nft.NewStorageQuanta.ShouldBe(4u);
	}

	[Fact]
	public void Counts_the_rows_the_token_metadata_adds()
	{
		// pre_burn creates the burnt counter at once and an inflation schedule the next-inflation row;
		// both are named fields of the token metadata, which the plan reads.
		var fields = new List<VmNamedDynamicVariable>
		{
			new() { name = new SmallString("name"), value = new VmDynamicVariable("Plan probe") },
			new() { name = new SmallString("icon"), value = new VmDynamicVariable("data:image/png;base64,iVBORw0KGgo=") },
			new() { name = new SmallString("url"), value = new VmDynamicVariable("https://example.invalid/p") },
			new() { name = new SmallString("description"), value = new VmDynamicVariable("x") },
			new() { name = StandardMeta.Token.pre_burn, value = new VmDynamicVariable(1000L) },
			new() { name = StandardMeta.Token.inflation_period, value = new VmDynamicVariable(-1L) }
		};
		fields.Sort((a, b) => a.name.CompareTo(b.name));
		var metadata = CarbonBlob.Serialize(new VmDynamicStruct { fields = fields.ToArray() });
		var plan = FeePlanner.Plan(TokenCall(TokenContract_Methods.CreateToken, CarbonBlob.Serialize(TokenInfo(false, metadata))), Config, OneWitness);
		plan.NewStorageQuanta.ShouldBe(5u);
	}

	// Staking metadata costs a token creation lookups the plan reads out of the same struct: a
	// staking organisation is looked up and a reward token is read, one query fee each.
	[Fact]
	public void Counts_the_lookups_the_staking_metadata_costs()
	{
		byte[] Metadata(params VmNamedDynamicVariable[] extra)
		{
			var fields = new List<VmNamedDynamicVariable>
			{
				new() { name = new SmallString("name"), value = new VmDynamicVariable("Plan probe") },
				new() { name = new SmallString("icon"), value = new VmDynamicVariable("data:image/png;base64,iVBORw0KGgo=") },
				new() { name = new SmallString("url"), value = new VmDynamicVariable("https://example.invalid/p") },
				new() { name = new SmallString("description"), value = new VmDynamicVariable("x") }
			};
			fields.AddRange(extra);
			fields.Sort((a, b) => a.name.CompareTo(b.name));
			return CarbonBlob.Serialize(new VmDynamicStruct { fields = fields.ToArray() });
		}
		FeePlan PlanOf(byte[] metadata) =>
			FeePlanner.Plan(TokenCall(TokenContract_Methods.CreateToken, CarbonBlob.Serialize(TokenInfo(false, metadata))), Config, OneWitness);
		var policy = 100_000_000_000_000UL + (100_000_000_000_000UL >> 2); // 3-char symbol

		var plain = PlanOf(Metadata());
		plain.ExpectedGasBill.ShouldBe(Bill(0, plain.EnvelopeBytes + 3 + 8, policy));
		var org = PlanOf(Metadata(new VmNamedDynamicVariable { name = StandardMeta.Token.staking_org_id, value = new VmDynamicVariable(3L) }));
		org.ExpectedGasBill.ShouldBe(Bill(10, org.EnvelopeBytes + 3 + 8, policy));
		var reward = PlanOf(Metadata(new VmNamedDynamicVariable { name = StandardMeta.Token.staking_reward_token, value = new VmDynamicVariable(97L) }));
		reward.ExpectedGasBill.ShouldBe(Bill(10, reward.EnvelopeBytes + 3 + 8, policy));
		var both = PlanOf(Metadata(
			new VmNamedDynamicVariable { name = StandardMeta.Token.staking_org_id, value = new VmDynamicVariable(3L) },
			new VmNamedDynamicVariable { name = StandardMeta.Token.staking_reward_token, value = new VmDynamicVariable(97L) }));
		both.ExpectedGasBill.ShouldBe(Bill(20, both.EnvelopeBytes + 3 + 8, policy));
	}

	[Fact]
	public void Reads_the_series_being_created_out_of_the_call()
	{
		var seriesInfo = SeriesInfoBuilder.Build(BigInteger.Parse("77"), 100, 100, PayerPub);
		var args = CarbonBlob.Serialize(new CreateTokenSeriesArgs { tokenId = 9, info = seriesInfo });
		var plan = FeePlanner.Plan(TokenCall(TokenContract_Methods.CreateTokenSeries, args), Config, OneWitness);

		plan.Kinds.ShouldBe(new[] { NativeFeeKind.CreateTokenSeries });
		// Series info + series supply + the meta-id lookup the SDK-built metadata always carries.
		plan.NewStorageQuanta.ShouldBe(3u);
		plan.ExpectedGasBill.ShouldBe(Bill(0, plan.EnvelopeBytes + 3 + 4, 25_000_000_000_000));
		FeePlanner.Plan(TokenCall(TokenContract_Methods.CreateTokenSeries, args), Config, new FeePlanOptions { WitnessCount = 1, SeriesHasMetaId = false })
			.NewStorageQuanta.ShouldBe(2u);
	}

	// DuplicatedSeries = false is stated because the bill below is the unique-series one: the mode is
	// chain state no message carries, so an unspecified plan takes the costlier duplicated reading.
	[Fact]
	public void Reads_every_minted_instance_out_of_a_Phantasma_mint_call()
	{
		var msg = PhantasmaMintCall(new[] { PhantasmaMint(7, 182) });
		var plan = FeePlanner.Plan(msg, Config, new FeePlanOptions { WitnessCount = 1, DuplicatedSeries = false, SupplyRowExists = true });
		plan.Kinds.ShouldBe(new[] { NativeFeeKind.MintPhantasmaNonFungible });
		plan.NewStorageQuanta.ShouldBe(5u);
		plan.ExpectedGasBill.ShouldBe(Bill(30, plan.EnvelopeBytes + 5 + 44));
		plan.MaxData.ShouldBe(1_000_000UL);
	}

	// A multi-instance mint prices every instance: the work and the two query fees each one costs,
	// the 40 result bytes it returns, and its own storage rows - while the recipient's balance row is
	// paid for once. Reading the count out of the call is what makes that possible.
	[Fact]
	public void Prices_every_instance_of_a_multi_instance_Phantasma_mint()
	{
		var msg = PhantasmaMintCall(new[] { PhantasmaMint(7, 182), PhantasmaMint(7, 182), PhantasmaMint(7, 182) });
		var plan = FeePlanner.Plan(msg, Config, new FeePlanOptions { WitnessCount = 1, DuplicatedSeries = false, SupplyRowExists = true });

		plan.NewStorageQuanta.ShouldBe(1u + 3 * 4);
		plan.ExpectedGasBill.ShouldBe(Bill(30 * 3, plan.EnvelopeBytes + 13 + 4 + 40 * 3));
		plan.MaxData.ShouldBe(13UL * Config.dataEscrowPerRow);
	}

	// A duplicated series costs one query more per instance than a unique one, and one more for the
	// series itself: the chain reads that series' supply once per transaction, not once per instance.
	[Fact]
	public void Prices_a_duplicated_series_per_instance_and_its_supply_once()
	{
		var facts = new FeePlanOptions { WitnessCount = 1, DuplicatedSeries = true, SupplyRowExists = true };
		var plan = FeePlanner.Plan(PhantasmaMintCall(new[] { PhantasmaMint(7, 182), PhantasmaMint(7, 182), PhantasmaMint(7, 182) }), Config, facts);
		// (transfer + 3 queries) per instance, plus one query for the single series they share.
		plan.ExpectedGasBill.ShouldBe(Bill(40 * 3 + 10, plan.EnvelopeBytes + 13 + 4 + 40 * 3));
		// The same three instances spread over three series pay the supply read three times.
		var spread = FeePlanner.Plan(PhantasmaMintCall(new[] { PhantasmaMint(7, 182), PhantasmaMint(8, 182), PhantasmaMint(9, 182) }), Config, facts);
		(spread.ExpectedGasBill - plan.ExpectedGasBill).ShouldBe(20UL * 10_000);
	}

	// The series mode is not in the message and the planner offers the bill with no headroom, so an
	// unspecified plan has to assume the mode that costs more.
	[Fact]
	public void Plans_a_Phantasma_mint_as_duplicated_when_the_series_mode_is_not_stated()
	{
		var msg = PhantasmaMintCall(new[] { PhantasmaMint(7, 182), PhantasmaMint(7, 182), PhantasmaMint(7, 182) });
		var assumed = FeePlanner.Plan(msg, Config, OneWitness);
		var duplicated = FeePlanner.Plan(msg, Config, new FeePlanOptions { WitnessCount = 1, DuplicatedSeries = true });
		var unique = FeePlanner.Plan(msg, Config, new FeePlanOptions { WitnessCount = 1, DuplicatedSeries = false });
		assumed.ExpectedGasBill.ShouldBe(duplicated.ExpectedGasBill);
		assumed.ExpectedGasBill.ShouldBeGreaterThan(unique.ExpectedGasBill);
	}

	// The supply row is chain state with two absent-row edges - a limited token fully in circulation
	// before its first burn, an unlimited one with nothing outstanding before its next mint - and the
	// transaction that hits an edge recreates the row. An unstated plan covers that quantum in the
	// offer and the escrow ceiling; saying the row exists gives the exact quote.
	[Fact]
	public void Plans_a_mint_or_burn_to_cover_the_supply_row_unless_it_is_said_to_exist()
	{
		var burn = Msg(TxTypes.BurnFungible, new TxMsgBurnFungible { tokenId = 97, amount = new IntX(5) });
		var assumed = FeePlanner.Plan(burn, Config);
		var inPlace = FeePlanner.Plan(burn, Config, new FeePlanOptions { SupplyRowExists = true });
		assumed.NewStorageQuanta.ShouldBe(inPlace.NewStorageQuanta + 1);
		(assumed.MaxData - inPlace.MaxData).ShouldBe(Config.dataEscrowPerRow);
	}

	// What a burned NFT holds is chain state with no costlier reading - it can hold anything - so
	// the planner demands the list instead of assuming an empty address; an empty list is the
	// statement that nothing is infused. The RPC-side planner fills it in from the chain.
	[Fact]
	public void Demands_what_a_burned_NFT_holds_and_prices_its_return()
	{
		var burn = Msg(TxTypes.BurnNonFungible, new TxMsgBurnNonFungible { tokenId = 9, instanceId = 5 });
		Should.Throw<ArgumentException>(() => FeePlanner.Plan(burn, Config)).Message.ShouldContain("Infusions");

		var empty = FeePlanner.Plan(burn, Config, new FeePlanOptions { Infusions = Array.Empty<InfusedAsset>(), TokenBurnedBefore = true, SupplyRowExists = true });
		empty.Kinds.ShouldBe(new[] { NativeFeeKind.BurnNonFungible });
		empty.ExpectedGasBill.ShouldBe(Bill(30, empty.EnvelopeBytes));

		var returned = FeePlanner.Plan(burn, Config, new FeePlanOptions
		{
			Infusions = new[] { new InfusedAsset { TokenId = 1 }, new InfusedAsset { TokenId = 97 }, new InfusedAsset { TokenId = 9, NonFungible = true, InstanceCount = 2 } },
			TokenBurnedBefore = true,
			SupplyRowExists = true
		});
		// KCAL and the custom token: a transfer and a query each; the NFT: a query, two transfers, a query.
		(returned.ExpectedGasBill - empty.ExpectedGasBill).ShouldBe(80UL * 10_000);
		// The custom token's balance row, the NFT token's balance row and its two moved lookups.
		(returned.MaxData - empty.MaxData).ShouldBe(4UL * Config.dataEscrowPerRow);
	}

	// Whether the recipient is an NFT-derived address decides one query fee, and the address is in
	// the message, so the planner reads it there instead of asking. A user address adds nothing; the
	// NFT form does; a zero instance id is not an NFT address, exactly as the chain reads it.
	[Fact]
	public void Reads_the_NFT_address_recipient_out_of_the_message_and_prices_its_owner_lookup()
	{
		ulong ToAddress(Bytes32 to) => FeePlanner.Plan(Transfer(Config.gasTokenId, to), Config).ExpectedGasBill;
		var plain = ToAddress(OwnerPub);
		(ToAddress(TokenHelper.GetNftAddress(9, 1)) - plain).ShouldBe(100_000UL);
		ToAddress(TokenHelper.GetNftAddress(9, 0)).ShouldBe(plain);

		// Every mint kind pays the same owner lookup once per call; the Phantasma call carries its
		// recipient in the arguments and the planner reads it out of there just the same.
		var options = new FeePlanOptions { WitnessCount = 1, DuplicatedSeries = false };
		var infused = FeePlanner.Plan(PhantasmaMintCall(new[] { PhantasmaMint(7, 182) }, TokenHelper.GetNftAddress(9, 1)), Config, options);
		var normal = FeePlanner.Plan(PhantasmaMintCall(new[] { PhantasmaMint(7, 182) }), Config, options);
		(infused.ExpectedGasBill - normal.ExpectedGasBill).ShouldBe(100_000UL);

		var mint = Msg(TxTypes.MintFungible, new TxMsgMintFungible { tokenId = 97, to = TokenHelper.GetNftAddress(9, 1), amount = new IntX(5) });
		var mintPlain = Msg(TxTypes.MintFungible, new TxMsgMintFungible { tokenId = 97, to = OwnerPub, amount = new IntX(5) });
		(FeePlanner.Plan(mint, Config).ExpectedGasBill - FeePlanner.Plan(mintPlain, Config).ExpectedGasBill).ShouldBe(100_000UL);
	}

	// The multi-instance NFT transfer is the mirror case: each instance deletes the sender's lookup
	// row and creates the recipient's, so only the recipient's new balance row is billed, while the
	// escrow ceiling still has to cover every row the transaction may create.
	[Fact]
	public void Prices_every_instance_of_a_multi_instance_NFT_transfer()
	{
		var msg = Msg(TxTypes.TransferNonFungible_Multi, new TxMsgTransferNonFungible_Multi { to = OwnerPub, tokenId = 97, instanceIds = new ulong[] { 11, 12 } });
		var plan = FeePlanner.Plan(msg, Config);
		plan.Kinds.ShouldBe(new[] { NativeFeeKind.TransferNonFungible });
		plan.NewStorageQuanta.ShouldBe(3u);
		plan.DeletedStorageQuanta.ShouldBe(2u);
		plan.ExpectedGasBill.ShouldBe(Bill(20, plan.EnvelopeBytes + 1));
		plan.MaxData.ShouldBe(3UL * Config.dataEscrowPerRow);
	}

	// Built as the raw wire message: the chain still admits the type where its token config allows
	// it, but the SDK ships no builder for it.
	[Fact]
	public void Plans_a_raw_NFT_mint_with_its_ROM_as_stored()
	{
		var msg = Msg(TxTypes.MintNonFungible, new TxMsgMintNonFungible { tokenId = 9, seriesId = 1, to = OwnerPub, rom = new byte[100], ram = Array.Empty<byte>() });
		var plan = FeePlanner.Plan(msg, Config, new FeePlanOptions { SupplyRowExists = true });
		plan.Kinds.ShouldBe(new[] { NativeFeeKind.MintNonFungible });
		plan.NewStorageQuanta.ShouldBe(5u);
		FeePlanner.Plan(msg, Config, new FeePlanOptions { SupplyRowExists = true, RomHasMetaId = false }).NewStorageQuanta.ShouldBe(4u);
		plan.ExpectedGasBill.ShouldBe(Bill(10, plan.EnvelopeBytes + 5 + 12));
	}

	[Fact]
	public void Recognises_a_name_registration()
	{
		using var stream = new MemoryStream();
		using var w = new BinaryWriter(stream);
		w.Write32(PayerPub);
		w.Write(new SmallString("alice"));
		var msg = Msg(TxTypes.Call, new TxMsgCall { moduleId = (uint)ModuleId.Governance, methodId = (uint)GovernanceContract_Methods.RegisterName, args = stream.ToArray() });

		var plan = FeePlanner.Plan(msg, Config, OneWitness);
		plan.Kinds.ShouldBe(new[] { NativeFeeKind.RegisterName });
		plan.ExpectedGasBill.ShouldBe(Bill(0, plan.EnvelopeBytes, 100_000_000_000_000_000UL >> 4));
		plan.MaxData.ShouldBe(0UL);
	}

	[Fact]
	public void Budgets_an_allowance_for_scripts_and_unmodelled_calls_instead_of_guessing()
	{
		var script = Msg(TxTypes.Phantasma, new TxMsgPhantasma { nexus = new SmallString("simnet"), chain = new SmallString("main"), script = new byte[10] });
		var plan = FeePlanner.Plan(script, Config, OneWitness);
		plan.Kinds.ShouldBe(new[] { NativeFeeKind.Script });
		plan.ExpectedGasBill.ShouldBe(Bill(5000, plan.EnvelopeBytes + 4 + 512));
		var tighter = FeePlanner.Plan(script, Config, new FeePlanOptions { WitnessCount = 1, ScriptUnitsAllowance = 1000, ScriptEventBytes = 0, ScriptStorageQuanta = 0 });
		tighter.ExpectedGasBill.ShouldBe(Bill(1000, plan.EnvelopeBytes));

		// A token call the planner has no formula for is budgeted the same way rather than refused.
		var unmodelled = TokenCall(TokenContract_Methods.UpdateTokenMetadata, new byte[20]);
		FeePlanner.Plan(unmodelled, Config, OneWitness).Kinds.ShouldBe(new[] { NativeFeeKind.Script });
	}

	[Fact]
	public void Sizes_a_call_for_the_number_of_witnesses_that_will_sign_it()
	{
		var msg = TokenCall(TokenContract_Methods.CreateToken, CarbonBlob.Serialize(TokenInfo(false)));
		var one = FeePlanner.Plan(msg, Config, OneWitness);
		var two = FeePlanner.Plan(msg, Config, new FeePlanOptions { WitnessCount = 2 });
		(two.EnvelopeBytes - one.EnvelopeBytes).ShouldBe(96);
		(two.ExpectedGasBill - one.ExpectedGasBill).ShouldBe(96UL * 25 * 10_000);
		TxMsgSigner.SignAndSerializeWithKeys(two.Apply(msg), Payer, Owner).Length.ShouldBe(two.EnvelopeBytes);
	}

	// A wallet that performs several token operations at once sends them as module calls: one
	// Token.* call, or a Call_Multi of them. The chain runs those through the very same contract
	// methods the native transaction types run, in a plain loop, with no per-call surcharge - so the
	// planner has to price them like the native operations they are instead of budgeting them as if
	// they were VM scripts.
	private static TxMsgCall BurnNftCall(ulong tokenId, ulong[] instanceIds) => new()
	{
		moduleId = (uint)ModuleId.Token,
		methodId = (uint)TokenContract_Methods.BurnNonFungible,
		args = CarbonBlob.Serialize(new BurnNonFungibleArgs { tokenId = tokenId, from = PayerPub, instanceIds = instanceIds })
	};

	private static TxMsg BurnBatch(int count) =>
		Msg(TxTypes.Call_Multi, new TxMsgCall_Multi
		{
			calls = Enumerable.Range(0, count).Select(i => BurnNftCall(9, new[] { (ulong)(11 + i) })).ToArray()
		});

	private static FeePlanOptions BurnFacts() => new()
	{
		WitnessCount = 1,
		Infusions = Array.Empty<InfusedAsset>(),
		TokenBurnedBefore = true,
		SupplyRowExists = true
	};

	[Fact]
	public void Prices_a_fungible_transfer_call_exactly_as_the_native_transfer()
	{
		var call = TokenCall(TokenContract_Methods.TransferFungible,
			CarbonBlob.Serialize(new TransferFungibleArgs { to = OwnerPub, from = PayerPub, tokenId = 97, amount = new IntX(5) }));
		var plan = FeePlanner.Plan(call, Config, OneWitness);
		plan.Kinds.ShouldBe(new[] { NativeFeeKind.TransferFungible });
		// One transfer fee, and the fresh recipient row that the costlier default assumes.
		plan.ExpectedGasBill.ShouldBe(Bill(10, plan.EnvelopeBytes + 1));
		plan.MaxData.ShouldBe(Config.dataEscrowPerRow);

		// The recipient's address form decides one query fee here exactly as it does natively.
		var infused = TokenCall(TokenContract_Methods.TransferFungible,
			CarbonBlob.Serialize(new TransferFungibleArgs { to = TokenHelper.GetNftAddress(9, 5), from = PayerPub, tokenId = 97, amount = new IntX(5) }));
		FeePlanner.Plan(infused, Config, OneWitness).ExpectedGasBill.ShouldBe(Bill(20, plan.EnvelopeBytes + 1));
	}

	[Fact]
	public void Counts_the_instances_of_an_NFT_transfer_call()
	{
		var call = TokenCall(TokenContract_Methods.TransferNonFungible,
			CarbonBlob.Serialize(new TransferNonFungibleArgs { to = OwnerPub, from = PayerPub, tokenId = 97, instanceIds = new ulong[] { 11, 12, 13 } }));
		var plan = FeePlanner.Plan(call, Config, OneWitness);
		plan.Kinds.ShouldBe(new[] { NativeFeeKind.TransferNonFungible });
		// Three transfer fees; three lookup rows created against three deleted, plus the fresh
		// recipient balance row - so one net quantum of block data and four rows of ceiling.
		plan.ExpectedGasBill.ShouldBe(Bill(30, plan.EnvelopeBytes + 1));
		plan.NewStorageQuanta.ShouldBe(4u);
		plan.DeletedStorageQuanta.ShouldBe(3u);
	}

	[Fact]
	public void Prices_fungible_mint_and_burn_calls_with_their_variable_length_result()
	{
		var mint = TokenCall(TokenContract_Methods.MintFungible,
			CarbonBlob.Serialize(new MintFungibleArgs { tokenId = 97, to = OwnerPub, amount = new IntX(1000) }));
		var mintPlan = FeePlanner.Plan(mint, Config, new FeePlanOptions { WitnessCount = 1, BigFungible = false });
		mintPlan.Kinds.ShouldBe(new[] { NativeFeeKind.MintFungible });
		// Transfer fee; the recipient's row and the supply row; a 9-byte int64 balance answer.
		mintPlan.ExpectedGasBill.ShouldBe(Bill(10, mintPlan.EnvelopeBytes + 2 + 9));

		var burn = TokenCall(TokenContract_Methods.BurnFungible,
			CarbonBlob.Serialize(new BurnFungibleArgs { tokenId = 97, from = PayerPub, amount = new IntX(1) }));
		var burnPlan = FeePlanner.Plan(burn, Config, new FeePlanOptions { WitnessCount = 1, BigFungible = false, TokenBurnedBefore = true, SupplyRowExists = true });
		burnPlan.Kinds.ShouldBe(new[] { NativeFeeKind.BurnFungible });
		burnPlan.ExpectedGasBill.ShouldBe(Bill(10, burnPlan.EnvelopeBytes + 9));
	}

	[Fact]
	public void Prices_an_NFT_burn_call_for_every_instance_it_names_and_demands_what_they_hold()
	{
		var call = Msg(TxTypes.Call, BurnNftCall(9, new ulong[] { 11, 12 }));
		Should.Throw<ArgumentException>(() => FeePlanner.Plan(call, Config, OneWitness));

		var plan = FeePlanner.Plan(call, Config, BurnFacts());
		plan.Kinds.ShouldBe(new[] { NativeFeeKind.BurnNonFungible });
		// Two instances at a transfer fee plus two queries each; four rows deleted per instance.
		plan.ExpectedGasBill.ShouldBe(Bill(60, plan.EnvelopeBytes));
		plan.DeletedStorageQuanta.ShouldBe(8u);
	}

	// Explicit NFT mints are refused chain-wide by mainnet SR 50 whichever way they arrive, so the
	// SDK removed its native builder and does not price the call either: there is no transaction to
	// price. It must stay budgeted rather than quietly grow a model.
	[Fact]
	public void Leaves_an_explicit_NFT_mint_call_at_the_script_budget()
	{
		var mint = TokenCall(TokenContract_Methods.MintNonFungible,
			CarbonBlob.Serialize(new MintNonFungibleArgs { tokenId = 97, address = OwnerPub, tokens = Array.Empty<NftMintInfo>() }));
		FeePlanner.Plan(mint, Config, OneWitness).Kinds.ShouldBe(new[] { NativeFeeKind.Script });
	}

	// A call may name argument SECTIONS instead of arguments: the chain assembles them at execution
	// out of earlier calls' results. Nothing to read a price from, and reading the empty argument
	// buffer as if it held the fields would be worse than budgeting.
	[Fact]
	public void Budgets_a_call_whose_arguments_are_assembled_at_execution()
	{
		var call = BurnNftCall(9, new ulong[] { 11 });
		call.args = Array.Empty<byte>();
		call.sections = new MsgCallArgSections
		{
			argSections = new[] { new MsgCallArgs { registerOffset = -1, args = Array.Empty<byte>() } }
		};
		FeePlanner.Plan(Msg(TxTypes.Call, call), Config, OneWitness).Kinds.ShouldBe(new[] { NativeFeeKind.Script });
	}

	// The reference point is a measured one: two NFT burns batched this way settled at 73,100,000
	// kcal-base against a 290-byte envelope on the localnet (evidence cs-live-v6, 2026-09-02), which
	// is exactly what summing the two burn models and counting the envelope once produces.
	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(11)]
	[InlineData(20)]
	public void Prices_a_batch_as_the_sum_of_its_calls_with_the_envelope_counted_once(int count)
	{
		var plan = FeePlanner.Plan(BurnBatch(count), Config, BurnFacts());
		plan.Kinds.ShouldBe(Enumerable.Repeat(NativeFeeKind.BurnNonFungible, count).ToArray());
		// Per burn: a transfer fee and two queries. Every burn deletes more rows than it creates, so
		// the batch adds no block data beyond its envelope.
		plan.ExpectedGasBill.ShouldBe(Bill(30UL * (ulong)count, plan.EnvelopeBytes));
		plan.DeletedStorageQuanta.ShouldBe((uint)(4 * count));
	}

	// The defect these batch cases exist for: a Call_Multi used to take the flat script budget
	// whatever it contained, so its price did not follow the work it does. It must now.
	[Fact]
	public void Follows_the_work_of_the_batch_instead_of_a_flat_budget()
	{
		var two = FeePlanner.Plan(BurnBatch(2), Config, BurnFacts());
		var twenty = FeePlanner.Plan(BurnBatch(20), Config, BurnFacts());
		var perBurn = (twenty.ExpectedGasBill - two.ExpectedGasBill) / 18;
		var envelopePerBurn = (twenty.EnvelopeBytes - two.EnvelopeBytes) / 18;
		perBurn.ShouldBe(Bill(30, envelopePerBurn));
	}

	[Fact]
	public void Prices_what_the_batch_gives_back_once_not_once_per_burn()
	{
		var empty = FeePlanner.Plan(BurnBatch(2), Config, BurnFacts());
		var facts = BurnFacts();
		facts.Infusions = new[] { new InfusedAsset { TokenId = 1 }, new InfusedAsset { TokenId = 97 } };
		var returned = FeePlanner.Plan(BurnBatch(2), Config, facts);
		// One transfer plus one owner-lookup query per returned token, counted for the batch and not
		// for each of its burns; the custom token's balance row is recreated for the burner.
		(returned.ExpectedGasBill - empty.ExpectedGasBill).ShouldBe(40UL * 10_000);
		(returned.MaxData - empty.MaxData).ShouldBe(Config.dataEscrowPerRow);
	}

	[Fact]
	public void Prices_the_calls_it_models_and_budgets_only_the_ones_it_does_not()
	{
		var unknown = new TxMsgCall { moduleId = (uint)ModuleId.Token, methodId = (uint)TokenContract_Methods.UpdateTokenMetadata, args = new byte[8] };
		var msg = Msg(TxTypes.Call_Multi, new TxMsgCall_Multi { calls = new[] { BurnNftCall(9, new ulong[] { 11 }), unknown } });

		var facts = BurnFacts();
		facts.ScriptUnitsAllowance = 100;
		var plan = FeePlanner.Plan(msg, Config, facts);
		plan.Kinds.ShouldBe(new[] { NativeFeeKind.BurnNonFungible, NativeFeeKind.Script });
		// Both work terms, and the budget's event bytes. Its four storage quanta are NOT block data
		// here: the chain bills the net growth of the whole transaction, and the burn deletes four
		// rows against them - which is the reason a batch is settled once instead of call by call.
		plan.ExpectedGasBill.ShouldBe(Bill(130, plan.EnvelopeBytes + 512));
		plan.NewStorageQuanta.ShouldBe(4u);
		plan.DeletedStorageQuanta.ShouldBe(4u);
	}

	[Fact]
	public void Names_every_instance_a_message_burns_whichever_shape_it_burns_them_in()
	{
		var native = Msg(TxTypes.BurnNonFungible, new TxMsgBurnNonFungible { tokenId = 9, instanceId = 5 });
		FeePlanner.BurnedInstances(native).ShouldBe(new[] { (9UL, 5UL) });
		FeePlanner.BurnedInstances(BurnBatch(2)).ShouldBe(new[] { (9UL, 11UL), (9UL, 12UL) });
		FeePlanner.BurnedInstances(Transfer(97)).ShouldBeEmpty();
	}

	// The coverage gate this file owes the model: every message type the SDK carries is named here
	// with what the planner makes of it, and the table is asserted to be exhaustive over TxTypes. A
	// new message type therefore cannot be added and quietly fall into the script budget - this test
	// fails until someone states what its fee is. A Script entry below is a decision, not an omission.
	private static byte[] BurnFungibleArgsBytes() =>
		CarbonBlob.Serialize(new BurnFungibleArgs { tokenId = 97, from = PayerPub, amount = new IntX(1) });

	public static TheoryData<TxTypes, TxMsg, NativeFeeKind[]?> PlannedTypes()
	{
		var burnCall = new TxMsgCall { moduleId = (uint)ModuleId.Token, methodId = (uint)TokenContract_Methods.BurnFungible, args = BurnFungibleArgsBytes() };
		var data = new TheoryData<TxTypes, TxMsg, NativeFeeKind[]?>
		{
			{ TxTypes.Call, Msg(TxTypes.Call, burnCall), new[] { NativeFeeKind.BurnFungible } },
			{ TxTypes.Call_Multi, Msg(TxTypes.Call_Multi, new TxMsgCall_Multi { calls = new[] { burnCall, burnCall } }), new[] { NativeFeeKind.BurnFungible, NativeFeeKind.BurnFungible } },
			// A Trade packs its operations into named arrays rather than calls; nothing reads them
			// yet, so it is budgeted. Modelling it is worth doing only when something builds one.
			// The arrays are given explicitly: TxMsgTrade.Write dereferences them, so a default-
			// constructed one cannot be serialized and therefore cannot be sized either.
			{ TxTypes.Trade, Msg(TxTypes.Trade, new TxMsgTrade
				{
					transferF = Array.Empty<TxMsgTransferFungible_GasPayer>(),
					transferN = Array.Empty<TxMsgTransferNonFungible_Single_GasPayer>(),
					mintF = Array.Empty<TxMsgMintFungible>(),
					burnF = Array.Empty<TxMsgBurnFungible_GasPayer>(),
					mintN = Array.Empty<TxMsgMintNonFungible>(),
					burnN = Array.Empty<TxMsgBurnNonFungible_GasPayer>()
				}), new[] { NativeFeeKind.Script } },
			{ TxTypes.TransferFungible, Transfer(97), new[] { NativeFeeKind.TransferFungible } },
			{ TxTypes.TransferFungible_GasPayer, Msg(TxTypes.TransferFungible_GasPayer, new TxMsgTransferFungible_GasPayer { to = OwnerPub, from = PayerPub, tokenId = 97, amount = 1 }), new[] { NativeFeeKind.TransferFungible } },
			{ TxTypes.TransferNonFungible_Single, Msg(TxTypes.TransferNonFungible_Single, new TxMsgTransferNonFungible_Single { to = OwnerPub, tokenId = 9, instanceId = 5 }), new[] { NativeFeeKind.TransferNonFungible } },
			{ TxTypes.TransferNonFungible_Single_GasPayer, Msg(TxTypes.TransferNonFungible_Single_GasPayer, new TxMsgTransferNonFungible_Single_GasPayer { to = OwnerPub, from = PayerPub, tokenId = 9, instanceId = 5 }), new[] { NativeFeeKind.TransferNonFungible } },
			{ TxTypes.TransferNonFungible_Multi, Msg(TxTypes.TransferNonFungible_Multi, new TxMsgTransferNonFungible_Multi { to = OwnerPub, tokenId = 9, instanceIds = new ulong[] { 5, 6 } }), new[] { NativeFeeKind.TransferNonFungible } },
			{ TxTypes.TransferNonFungible_Multi_GasPayer, Msg(TxTypes.TransferNonFungible_Multi_GasPayer, new TxMsgTransferNonFungible_Multi_GasPayer { to = OwnerPub, from = PayerPub, tokenId = 9, instanceIds = new ulong[] { 5, 6 } }), new[] { NativeFeeKind.TransferNonFungible } },
			{ TxTypes.MintFungible, Msg(TxTypes.MintFungible, new TxMsgMintFungible { tokenId = 97, to = OwnerPub, amount = new IntX(1) }), new[] { NativeFeeKind.MintFungible } },
			{ TxTypes.BurnFungible, Msg(TxTypes.BurnFungible, new TxMsgBurnFungible { tokenId = 97, amount = new IntX(1) }), new[] { NativeFeeKind.BurnFungible } },
			{ TxTypes.BurnFungible_GasPayer, Msg(TxTypes.BurnFungible_GasPayer, new TxMsgBurnFungible_GasPayer { tokenId = 97, from = PayerPub, amount = new IntX(1) }), new[] { NativeFeeKind.BurnFungible } },
			{ TxTypes.MintNonFungible, Msg(TxTypes.MintNonFungible, new TxMsgMintNonFungible { tokenId = 9, to = OwnerPub, seriesId = 1, rom = new byte[8], ram = Array.Empty<byte>() }), new[] { NativeFeeKind.MintNonFungible } },
			{ TxTypes.BurnNonFungible, Msg(TxTypes.BurnNonFungible, new TxMsgBurnNonFungible { tokenId = 9, instanceId = 5 }), new[] { NativeFeeKind.BurnNonFungible } },
			{ TxTypes.BurnNonFungible_GasPayer, Msg(TxTypes.BurnNonFungible_GasPayer, new TxMsgBurnNonFungible_GasPayer { tokenId = 9, from = PayerPub, instanceId = 5 }), new[] { NativeFeeKind.BurnNonFungible } },
			{ TxTypes.Phantasma, Msg(TxTypes.Phantasma, new TxMsgPhantasma { nexus = new SmallString("simnet"), chain = new SmallString("main"), script = new byte[8] }), new[] { NativeFeeKind.Script } },
			// A raw Gen2 envelope carries its own fee fields and its own signatures; this planner
			// prices Carbon messages, so it refuses rather than quoting a number it cannot size.
			{ TxTypes.Phantasma_Raw, Msg(TxTypes.Phantasma_Raw, new TxMsgPhantasma_Raw { transaction = new byte[8] }), null }
		};
		return data;
	}

	[Fact]
	public void Names_every_transaction_type_the_SDK_carries()
	{
		var named = PlannedTypes().Select(row => (TxTypes)row[0]).OrderBy(t => t).ToArray();
		var all = Enum.GetValues(typeof(TxTypes)).Cast<TxTypes>().OrderBy(t => t).ToArray();
		named.ShouldBe(all);
	}

	[Theory]
	[MemberData(nameof(PlannedTypes))]
	public void Plans_every_transaction_type_as_declared(TxTypes type, TxMsg msg, NativeFeeKind[]? expected)
	{
		// Only the witness-array types take a count; the rest fix their own witness set and refuse one.
		var options = new FeePlanOptions { Infusions = Array.Empty<InfusedAsset>() };
		if (SignedTxMsg.RequiredWitnesses(msg) == null)
			options.WitnessCount = 1;
		if (expected == null)
		{
			Should.Throw<ArgumentException>(() => FeePlanner.Plan(msg, Config, options))
				.Message.ShouldContain("Cannot plan fees");
			return;
		}
		FeePlanner.Plan(msg, Config, options).Kinds.ShouldBe(expected);
		type.ShouldBe(msg.type);
	}

	// A wallet has to choose between showing "0.0073 KCAL" and "up to 0.0073 KCAL", and it reads that
	// from one field. Exact is true only when nothing the plan had to assume could have moved the
	// number - which is a different question from whether any fact was left unstated, because most
	// facts do not enter most operations.
	[Fact]
	public void Is_exact_for_a_gas_token_transfer_whatever_the_caller_states()
	{
		// The chain's own token rows are free, so the recipient's row costs nothing and the fact the
		// caller did not state could not have changed the price. A wallet's commonest operation must
		// not be pushed into the "up to" branch by a fact that does not apply to it.
		FeePlanner.Plan(Transfer(Config.gasTokenId), Config).Exact.ShouldBeTrue();
		FeePlanner.Plan(Transfer(Config.dataTokenId), Config).Exact.ShouldBeTrue();
	}

	[Fact]
	public void Is_not_exact_when_an_unstated_fact_decided_part_of_the_price()
	{
		// A custom token's recipient row is paid, so RecipientHoldsToken decides one storage quantum.
		var custom = Transfer(97);
		FeePlanner.Plan(custom, Config).Exact.ShouldBeFalse();
		FeePlanner.Plan(custom, Config, new FeePlanOptions { RecipientHoldsToken = true }).Exact.ShouldBeTrue();
		FeePlanner.Plan(custom, Config, new FeePlanOptions { RecipientHoldsToken = false }).Exact.ShouldBeTrue();
	}

	// BigFungible = true does not say what the balance IS, it says the answer may be as wide as an
	// int256 - so the model prices 33 bytes and the chain writes fewer. Stating it does not make the
	// number a prediction, and the live matrix proved it: two rows that stated it settled below their
	// own plan.
	[Fact]
	public void Is_not_exact_when_the_plan_rests_on_the_widest_fungible_result()
	{
		var mint = Msg(TxTypes.MintFungible, new TxMsgMintFungible { tokenId = 97, to = OwnerPub, amount = new IntX(1) });
		FeePlanner.Plan(mint, Config, new FeePlanOptions { RecipientHoldsToken = true, SupplyRowExists = true, BigFungible = true })
			.Exact.ShouldBeFalse();
		FeePlanner.Plan(mint, Config, new FeePlanOptions { RecipientHoldsToken = true, SupplyRowExists = true, BigFungible = false })
			.Exact.ShouldBeTrue();
	}

	[Fact]
	public void Is_never_exact_when_part_of_the_message_had_to_be_budgeted()
	{
		var script = Msg(TxTypes.Phantasma, new TxMsgPhantasma { nexus = new SmallString("simnet"), chain = new SmallString("main"), script = new byte[40] });
		FeePlanner.Plan(script, Config, OneWitness).Exact.ShouldBeFalse();
	}

	[Fact]
	public void Is_exact_for_a_burn_that_states_the_two_facts_a_burn_reads()
	{
		var burn = Msg(TxTypes.BurnNonFungible, new TxMsgBurnNonFungible { tokenId = 9, instanceId = 5 });
		var empty = Array.Empty<InfusedAsset>();
		FeePlanner.Plan(burn, Config, new FeePlanOptions { Infusions = empty }).Exact.ShouldBeFalse();
		FeePlanner.Plan(burn, Config, new FeePlanOptions { Infusions = empty, TokenBurnedBefore = true, SupplyRowExists = true })
			.Exact.ShouldBeTrue();
		// The ROM's meta-id row is deleted by a burn, never created, and a burn deletes more rows than
		// it creates - so that fact cannot move a burn's bill and leaving it unstated does not make
		// the number an upper bound.
		FeePlanner.Plan(burn, Config, new FeePlanOptions { Infusions = empty, TokenBurnedBefore = true, SupplyRowExists = true, RomHasMetaId = false })
			.Exact.ShouldBeTrue();
	}

	// The flag is computed by pricing the message a second time with every unstated fact at its
	// cheaper reading. That only answers honestly if the "costlier default" claim on each fact is
	// true, so it is checked here: stating the other value must never raise the quote.
	[Fact]
	public void Prices_every_default_at_or_above_the_alternative_reading()
	{
		var mint = Msg(TxTypes.MintFungible, new TxMsgMintFungible { tokenId = 97, to = OwnerPub, amount = new IntX(1) });
		var burn = Msg(TxTypes.BurnFungible, new TxMsgBurnFungible { tokenId = 97, amount = new IntX(1) });
		var phantasma = PhantasmaMintCall(new[] { PhantasmaMint(1, 40), PhantasmaMint(2, 40) });
		var cases = new (TxMsg Msg, bool OpenWitness, FeePlanOptions[] Stated)[]
		{
			(Transfer(97), false, new[] { new FeePlanOptions { RecipientHoldsToken = true } }),
			(mint, false, new[]
			{
				new FeePlanOptions { RecipientHoldsToken = true },
				new FeePlanOptions { SupplyRowExists = true },
				new FeePlanOptions { BigFungible = false }
			}),
			(burn, false, new[]
			{
				new FeePlanOptions { TokenBurnedBefore = true },
				new FeePlanOptions { SupplyRowExists = true },
				new FeePlanOptions { BigFungible = false }
			}),
			(phantasma, true, new[]
			{
				new FeePlanOptions { WitnessCount = 1, RecipientHoldsToken = true },
				new FeePlanOptions { WitnessCount = 1, SupplyRowExists = true },
				new FeePlanOptions { WitnessCount = 1, DuplicatedSeries = false }
			})
		};
		foreach (var (msg, openWitness, stated) in cases)
		{
			var assumed = FeePlanner.Plan(msg, Config, openWitness ? OneWitness : null);
			foreach (var options in stated)
			{
				var told = FeePlanner.Plan(msg, Config, options);
				told.ExpectedGasBill.ShouldBeLessThanOrEqualTo(assumed.ExpectedGasBill);
				told.MaxData.ShouldBeLessThanOrEqualTo(assumed.MaxData);
			}
		}
	}
}

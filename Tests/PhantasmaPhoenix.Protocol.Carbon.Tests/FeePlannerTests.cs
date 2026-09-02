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

		plan.Kind.ShouldBe(NativeFeeKind.TransferFungible);
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

		plan.Kind.ShouldBe(NativeFeeKind.CreateToken);
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

		plan.Kind.ShouldBe(NativeFeeKind.CreateTokenSeries);
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
		plan.Kind.ShouldBe(NativeFeeKind.MintPhantasmaNonFungible);
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
		empty.Kind.ShouldBe(NativeFeeKind.BurnNonFungible);
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
		plan.Kind.ShouldBe(NativeFeeKind.TransferNonFungible);
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
		plan.Kind.ShouldBe(NativeFeeKind.MintNonFungible);
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
		plan.Kind.ShouldBe(NativeFeeKind.RegisterName);
		plan.ExpectedGasBill.ShouldBe(Bill(0, plan.EnvelopeBytes, 100_000_000_000_000_000UL >> 4));
		plan.MaxData.ShouldBe(0UL);
	}

	[Fact]
	public void Budgets_an_allowance_for_scripts_and_unmodelled_calls_instead_of_guessing()
	{
		var script = Msg(TxTypes.Phantasma, new TxMsgPhantasma { nexus = new SmallString("simnet"), chain = new SmallString("main"), script = new byte[10] });
		var plan = FeePlanner.Plan(script, Config, OneWitness);
		plan.Kind.ShouldBe(NativeFeeKind.Script);
		plan.ExpectedGasBill.ShouldBe(Bill(5000, plan.EnvelopeBytes + 4 + 512));
		var tighter = FeePlanner.Plan(script, Config, new FeePlanOptions { WitnessCount = 1, ScriptUnitsAllowance = 1000, ScriptEventBytes = 0, ScriptStorageQuanta = 0 });
		tighter.ExpectedGasBill.ShouldBe(Bill(1000, plan.EnvelopeBytes));

		// A token call the planner has no formula for is budgeted the same way rather than refused.
		var unmodelled = TokenCall(TokenContract_Methods.UpdateTokenMetadata, new byte[20]);
		FeePlanner.Plan(unmodelled, Config, OneWitness).Kind.ShouldBe(NativeFeeKind.Script);
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
}

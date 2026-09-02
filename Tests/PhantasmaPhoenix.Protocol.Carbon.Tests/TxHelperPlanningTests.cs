using System.Numerics;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

// Every BuildTxAndSign helper follows one rule: a message whose maxGas the caller already set is
// signed as it stands, and only an unplanned one is priced. The rule is read from the MESSAGE, not
// from the options argument, because the builders are what write the caller's limits into the
// message. This file holds every BuildTxAndSign helper to that one rule.
public class TxHelperPlanningTests
{
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
		gasFeePerByte = 250_000,
		gasBurnRatioMul = 1,
		minimumGasBill = 10_000_000,
		policyFeeCreateTokenBase = 100_000_000_000_000,
		policyFeeCreateTokenSymbol = 100_000_000_000_000,
		policyFeeCreateTokenSeries = 25_000_000_000_000,
		policyFeeRegisterName = 100_000_000_000_000_000,
		legacyDataEscrowPerRow = 2
	};

	private static readonly PhantasmaKeys Keys = PhantasmaKeys.FromWIF("KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d");
	private static readonly Bytes32 Owner = new(Keys.PublicKey);
	private static readonly Bytes32 Receiver = new(Enumerable.Repeat((byte)7, 32).ToArray());
	private static readonly GasConfig Config = MainnetConfig();

	// A gas offer no plan would ever produce, so "honoured" and "replanned" cannot be confused.
	private const ulong FixedMaxGas = 999_000_000;
	private const ulong FixedMaxData = 7_000_000;

	private static TxMsg Sent(byte[] bytes) => CarbonBlob.New<SignedTxMsg>(bytes).msg;

	private static TokenInfo TokenInfo() => TokenInfoBuilder.Build(
		"GPX",
		new IntX(0),
		false,
		2,
		Owner,
		TokenMetadataBuilder.BuildAndSerialize(new Dictionary<string, string>
		{
			["name"] = "Planning probe",
			["icon"] = "data:image/png;base64,iVBORw0KGgo=",
			["url"] = "https://example.invalid/p",
			["description"] = "x"
		}),
		null);

	private static SeriesInfo SeriesInfo() => SeriesInfoBuilder.Build(new BigInteger(7), 0, 0, Owner);

	private static PhantasmaNftMintParams PhantasmaMint() => new()
	{
		TokenId = 9,
		Sender = Owner,
		To = Receiver,
		Tokens = new[] { new PhantasmaNftMintInfo { phantasmaSeriesId = new IntX(new BigInteger(5)), rom = new byte[64], ram = Array.Empty<byte>() } }
	};

	private static readonly PlanAndSignOptions FixedLimits = new() { MaxGas = FixedMaxGas, MaxData = FixedMaxData };

	// Each entry signs the same helper twice: once with the caller's own limits, once with none.
	public static IEnumerable<object[]> Helpers()
	{
		yield return new object[]
		{
			"CreateTokenTxHelper",
			(Func<byte[]>)(() => CreateTokenTxHelper.BuildTxAndSign(TokenInfo(), Keys, Config, FixedLimits)),
			(Func<byte[]>)(() => CreateTokenTxHelper.BuildTxAndSign(TokenInfo(), Keys, Config))
		};
		yield return new object[]
		{
			"CreateTokenSeriesTxHelper",
			(Func<byte[]>)(() => CreateTokenSeriesTxHelper.BuildTxAndSign(9, SeriesInfo(), Keys, Config, FixedLimits)),
			(Func<byte[]>)(() => CreateTokenSeriesTxHelper.BuildTxAndSign(9, SeriesInfo(), Keys, Config))
		};
		yield return new object[]
		{
			"MintPhantasmaNonFungibleTxHelper",
			(Func<byte[]>)(() => MintPhantasmaNonFungibleTxHelper.BuildTxAndSign(PhantasmaMint(), Keys, Config, FixedLimits)),
			(Func<byte[]>)(() => MintPhantasmaNonFungibleTxHelper.BuildTxAndSign(PhantasmaMint(), Keys, Config))
		};
	}

	[Theory]
	[MemberData(nameof(Helpers))]
	public void Signs_the_callers_own_limits_unchanged(string name, Func<byte[]> withLimits, Func<byte[]> unplanned)
	{
		_ = unplanned;
		var msg = Sent(withLimits());
		msg.maxGas.ShouldBe(FixedMaxGas, name);
		msg.maxData.ShouldBe(FixedMaxData, name);
	}

	[Theory]
	[MemberData(nameof(Helpers))]
	public void Plans_a_message_that_carries_no_offer(string name, Func<byte[]> withLimits, Func<byte[]> unplanned)
	{
		_ = withLimits;
		var msg = Sent(unplanned());
		msg.maxGas.ShouldBeGreaterThan(0UL, name);
		msg.maxGas.ShouldNotBe(FixedMaxGas, name);
	}

	// The planned offer is the planner's own for the very message that was signed, and the signed
	// size is the size the plan was priced for.
	[Fact]
	public void Plans_exactly_what_the_planner_would_and_signs_at_the_planned_size()
	{
		var info = TokenInfo();
		var bytes = CreateTokenTxHelper.BuildTxAndSign(info, Keys, Config, new PlanAndSignOptions { Expiry = 1_787_000_000_000 });
		var plan = FeePlanner.Plan(CreateTokenTxHelper.BuildTx(info, Owner, new TxLimits { Expiry = 1_787_000_000_000 }), Config, new FeePlanOptions { WitnessCount = 1 });
		Sent(bytes).maxGas.ShouldBe(plan.MaxGas);
		Sent(bytes).maxData.ShouldBe(plan.MaxData);
		bytes.Length.ShouldBe(plan.EnvelopeBytes);
	}

	// State facts flow from the options into the plan: a mint that says the series is unique and the
	// supply row exists is priced exactly, one that says nothing is priced at the covering defaults.
	[Fact]
	public void Passes_the_stated_facts_through_to_the_plan()
	{
		var stated = Sent(MintPhantasmaNonFungibleTxHelper.BuildTxAndSign(PhantasmaMint(), Keys, Config,
			new PlanAndSignOptions { DuplicatedSeries = false, SupplyRowExists = true, Expiry = 1_787_000_000_000 }));
		var assumed = Sent(MintPhantasmaNonFungibleTxHelper.BuildTxAndSign(PhantasmaMint(), Keys, Config,
			new PlanAndSignOptions { Expiry = 1_787_000_000_000 }));
		assumed.maxGas.ShouldBeGreaterThan(stated.maxGas);
		(assumed.maxData - stated.maxData).ShouldBe(Config.dataEscrowPerRow);
	}
}

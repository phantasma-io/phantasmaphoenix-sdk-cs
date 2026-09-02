using Newtonsoft.Json;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using PhantasmaPhoenix.RPC.Models;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.RPC.Tests;

// The per-client planner reads the chain's config once, keeps it briefly, and prices messages with
// it; a wallet talking to two chains gets two planners and no shared state.
public class ChainFeePlannerTests
{
	private const string GasConfigJson = """
	{
	  "gasModelVersion": 2, "blockRateTarget": 2000, "expiryWindow": 3600000, "unitsPerBlockDataByte": 25,
	  "gasConfig": {
	    "version": 1, "maxNameLength": 255, "maxTokenSymbolLength": 255, "feeShift": 0, "maxStructureSize": 1048576,
	    "feeMultiplier": "10000", "gasTokenId": "1", "dataTokenId": "2", "minimumGasOffer": "10", "dataEscrowPerRow": "200000",
	    "gasFeeTransfer": "10", "gasFeeQuery": "10", "gasFeeCreateTokenBase": "10000000000", "gasFeeCreateTokenSymbol": "10000000000",
	    "gasFeeCreateTokenSeries": "2500000000", "gasFeePerByte": "250000", "gasFeeRegisterName": "10000000000000",
	    "gasBurnRatioMul": "1", "gasBurnRatioShift": 0, "minimumGasBill": "10000000",
	    "gasProducerRatioMul": "0", "gasProducerRatioShift": 0, "gasDappRatioMul": "0", "gasDappRatioShift": 0,
	    "policyFeeCreateTokenBase": "100000000000000", "policyFeeCreateTokenSymbol": "100000000000000",
	    "policyFeeCreateTokenSeries": "25000000000000", "policyFeeRegisterName": "100000000000000000", "legacyDataEscrowPerRow": "2"
	  }
	}
	""";

	private sealed class CountingSource : IGasConfigSource
	{
		public int Reads { get; private set; }
		public Task<GasConfigResult?> GetGasConfigAsync()
		{
			Reads++;
			return Task.FromResult(JsonConvert.DeserializeObject<GasConfigResult>(GasConfigJson));
		}
	}

	private static readonly Bytes32 Owner = new(PhantasmaKeys.FromWIF("KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d").PublicKey);
	private static readonly Bytes32 Receiver = new(Enumerable.Repeat((byte)7, 32).ToArray());

	// A source that also knows what NFTs hold, the way a PhantasmaAPI does.
	private sealed class HoldingsSource : IGasConfigSource, IInfusionSource
	{
		public int InfusionReads { get; private set; }
		public IReadOnlyList<InfusedAsset> Holdings { get; set; } = new[] { new InfusedAsset { TokenId = 1 }, new InfusedAsset { TokenId = 97 } };
		public Task<GasConfigResult?> GetGasConfigAsync() => Task.FromResult(JsonConvert.DeserializeObject<GasConfigResult>(GasConfigJson));
		public Task<IReadOnlyList<InfusedAsset>> InfusedAssetsAsync(ulong tokenId, ulong instanceId)
		{
			InfusionReads++;
			return Task.FromResult(Holdings);
		}
	}

	private static TxMsg Burn() =>
		NativeTxHelper.BurnNonFungible(new BurnNonFungibleParams { From = Owner, TokenId = 9, InstanceId = 5, Expiry = 1_787_000_000_000 });

	private static TxMsg Transfer() =>
		NativeTxHelper.TransferFungible(new TransferFungibleParams { From = Owner, To = Receiver, TokenId = 1, Amount = 5, Expiry = 1_787_000_000_000 });

	[Fact]
	public async Task Reads_the_gas_config_once_and_prices_messages_with_it()
	{
		var source = new CountingSource();
		var planner = new ChainFeePlanner(source);

		var plan = await planner.PlanAsync(Transfer());
		plan.Kind.ShouldBe(NativeFeeKind.TransferFungible);
		plan.ExpectedGasBill.ShouldBe(42_600_000UL);
		await planner.PlanAsync(Transfer());
		(await planner.ChainParamsAsync()).ExpiryWindow.ShouldBe(3_600_000u);
		(await planner.ChainParamsAsync()).GasModelVersion.ShouldBe(2u);
		source.Reads.ShouldBe(1);
	}

	[Fact]
	public async Task Reads_the_config_again_when_asked_when_invalidated_and_when_the_cache_expires()
	{
		var source = new CountingSource();
		var planner = new ChainFeePlanner(source, new ChainFeePlannerOptions { ConfigTtlMs = 60_000 });

		await planner.PlanAsync(Transfer());
		await planner.PlanAsync(Transfer(), new PlanRequestOptions { RefreshConfig = true });
		source.Reads.ShouldBe(2);
		planner.Invalidate();
		await planner.ConfigAsync();
		source.Reads.ShouldBe(3);

		var expiring = new ChainFeePlanner(source, new ChainFeePlannerOptions { ConfigTtlMs = 0 });
		await expiring.ConfigAsync();
		await expiring.ConfigAsync();
		source.Reads.ShouldBe(5);
	}

	[Fact]
	public async Task Plans_against_a_config_the_caller_holds_without_touching_the_source()
	{
		var source = new CountingSource();
		var planner = new ChainFeePlanner(source);
		var config = (await source.GetGasConfigAsync())!.ToGasConfig();
		source.Reads.ShouldBe(1);
		planner.PlanWith(config, Transfer()).ExpectedGasBill.ShouldBe(42_600_000UL);
		source.Reads.ShouldBe(1);
	}

	// A burn is planned for what the NFT holds: read from the source unless the caller stated it.
	[Fact]
	public async Task Reads_what_a_burned_NFT_holds_unless_the_caller_states_it()
	{
		var source = new HoldingsSource();
		var planner = new ChainFeePlanner(source);
		var read = await planner.PlanAsync(Burn());
		source.InfusionReads.ShouldBe(1);
		var stated = planner.PlanWith(await planner.ConfigAsync(), Burn(), new FeePlanOptions { Infusions = source.Holdings });
		read.ExpectedGasBill.ShouldBe(stated.ExpectedGasBill);

		var empty = await planner.PlanAsync(Burn(), new PlanRequestOptions { Infusions = Array.Empty<InfusedAsset>() });
		source.InfusionReads.ShouldBe(1);
		(read.ExpectedGasBill - empty.ExpectedGasBill).ShouldBe(40UL * 10_000);

		// A source that cannot read holdings still plans everything but an unstated burn.
		var blind = new ChainFeePlanner(new CountingSource());
		(await blind.PlanAsync(Transfer())).ExpectedGasBill.ShouldBe(42_600_000UL);
		(await Should.ThrowAsync<InvalidOperationException>(() => blind.PlanAsync(Burn()))).Message.ShouldContain("Infusions");
	}

	[Fact]
	public void Is_owned_by_the_RPC_client_one_per_client()
	{
		using var a = new PhantasmaAPI("http://localhost/a");
		using var b = new PhantasmaAPI("http://localhost/b");
		a.Fees.ShouldBeSameAs(a.Fees);
		a.Fees.ShouldNotBeSameAs(b.Fees);
	}

	// The v2 byte price is a constant of the gas model; a node reporting another one runs a model this
	// SDK does not implement, and the config must be refused rather than produce wrong offers.
	[Fact]
	public void Refuses_a_config_that_prices_block_data_differently()
	{
		var result = JsonConvert.DeserializeObject<GasConfigResult>(GasConfigJson)!;
		result.UnitsPerBlockDataByte = 30;
		Should.Throw<InvalidOperationException>(() => result.ToGasConfig()).Message.ShouldContain("30 gas units per byte");
		result.UnitsPerBlockDataByte = null;
		Should.NotThrow(() => result.ToGasConfig());
	}
}

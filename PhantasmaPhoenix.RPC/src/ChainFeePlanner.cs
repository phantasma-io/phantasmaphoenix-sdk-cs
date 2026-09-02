using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using PhantasmaPhoenix.RPC.Models;

namespace PhantasmaPhoenix.RPC;

/// <summary>Where a planner reads the chain's gas config from - a <see cref="PhantasmaAPI"/>, or anything shaped like one.</summary>
public interface IGasConfigSource
{
	Task<GasConfigResult?> GetGasConfigAsync();
}

public sealed class ChainFeePlannerOptions
{
	/// <summary>
	/// How long a fetched gas config is reused before it is read again. Prices change only by
	/// governance resolution, but a stale price under-offers every transaction until it is noticed,
	/// so the default is short: 60 seconds.
	/// </summary>
	public int ConfigTtlMs { get; set; } = 60_000;
}

public class PlanRequestOptions : FeePlanOptions
{
	/// <summary>Read the gas config again before planning, ignoring the cache.</summary>
	public bool RefreshConfig { get; set; }
}

/// <summary>
/// Chain parameters the fee flow needs that are not part of the on-chain <see cref="GasConfig"/>: they
/// describe the node's admission rules rather than its prices, and arrive in the same getGasConfig answer.
/// </summary>
public readonly struct ChainFeeParams
{
	/// <summary>
	/// The longest lifetime the chain admits for a transaction, in milliseconds - it refuses an expiry
	/// at or beyond <c>now + expiryWindow</c>. Feed it to <see cref="TxLimits.ExpiryWithin"/> when a
	/// person sits between building a transaction and signing it.
	/// </summary>
	public uint ExpiryWindow { get; }

	/// <summary>Target time between blocks, in milliseconds.</summary>
	public uint BlockRateTarget { get; }

	/// <summary>Gas model the node runs: 1 = the original fee model, 2 = gas model v2.</summary>
	public uint GasModelVersion { get; }

	public ChainFeeParams(uint expiryWindow, uint blockRateTarget, uint gasModelVersion)
	{
		ExpiryWindow = expiryWindow;
		BlockRateTarget = blockRateTarget;
		GasModelVersion = gasModelVersion;
	}
}

/// <summary>
/// Plans transaction fees against one chain: reads that chain's gas config from its RPC client, keeps
/// it for a short while, and prices messages with it. A <see cref="PhantasmaAPI"/> owns one as
/// <see cref="PhantasmaAPI.Fees"/>, so a process talking to several chains has one planner per chain
/// and no shared state.
/// </summary>
public sealed class ChainFeePlanner
{
	private sealed class Cached
	{
		public GasConfig Config;
		public ChainFeeParams Params;
		public long FetchedAt;
		public Cached(GasConfig config, ChainFeeParams @params, long fetchedAt) { Config = config; Params = @params; FetchedAt = fetchedAt; }
	}

	private readonly IGasConfigSource _source;
	private readonly int _ttlMs;
	private Cached? _cached;

	public ChainFeePlanner(IGasConfigSource source, ChainFeePlannerOptions? options = null)
	{
		_source = source ?? throw new ArgumentNullException(nameof(source));
		_ttlMs = options?.ConfigTtlMs ?? 60_000;
	}

	/// <summary>The chain's gas config, read from the source when the cached one is missing or expired.</summary>
	public async Task<GasConfig> ConfigAsync(bool refresh = false) => (await ReadAsync(refresh).ConfigureAwait(false)).Config;

	/// <summary>The chain's admission parameters, from the same answer and the same cache as <see cref="ConfigAsync"/>.</summary>
	public async Task<ChainFeeParams> ChainParamsAsync(bool refresh = false) => (await ReadAsync(refresh).ConfigureAwait(false)).Params;

	/// <summary>Forgets the cached config; the next plan reads it again.</summary>
	public void Invalidate() => _cached = null;

	/// <summary>Plans a message against the chain's current prices. See <see cref="FeePlanner.Plan"/>.</summary>
	public async Task<FeePlan> PlanAsync(TxMsg msg, PlanRequestOptions? options = null)
	{
		var config = await ConfigAsync(options?.RefreshConfig ?? false).ConfigureAwait(false);
		return FeePlanner.Plan(msg, config, options);
	}

	/// <summary>Plans a message against a config the caller already holds - no network, no cache.</summary>
	public FeePlan PlanWith(in GasConfig config, TxMsg msg, FeePlanOptions? options = null) => FeePlanner.Plan(msg, config, options);

	private async Task<Cached> ReadAsync(bool refresh)
	{
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var cached = _cached;
		if (!refresh && cached != null && now - cached.FetchedAt < _ttlMs)
			return cached;
		var result = await _source.GetGasConfigAsync().ConfigureAwait(false)
			?? throw new InvalidOperationException("getGasConfig returned no result");
		var entry = new Cached(
			result.ToGasConfig(),
			new ChainFeeParams(result.ExpiryWindow, result.BlockRateTarget, result.GasModelVersion),
			now);
		_cached = entry;
		return entry;
	}
}

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

/// <summary>
/// Explicit transaction limits a builder writes into the message.
/// <para>
/// Builders carry no prices. A message built without <see cref="MaxGas"/> has a zero gas offer, and
/// that marks it as not yet planned. Plan it with <c>PhantasmaAPI.Fees</c> or
/// <see cref="FeePlanner.Plan"/> before signing, or pass the offer here.
/// </para>
/// </summary>
public class TxLimits
{
	/// <summary>Gas offer in kcal-base (<c>TxMsg.maxGas</c>). Null means 0, which means
	/// unplanned.</summary>
	public ulong? MaxGas { get; set; }

	/// <summary>Storage-escrow ceiling in data-token atoms (<c>TxMsg.maxData</c>). Null means
	/// 0.</summary>
	public ulong? MaxData { get; set; }

	/// <summary>
	/// Expiry as a millisecond timestamp (<c>TxMsg.expiry</c>). Null means
	/// <see cref="DefaultExpiryMs"/> from now.
	/// <para>
	/// A flow with a person in it should set this from the chain's own window instead. Examples are a
	/// hardware wallet confirming and a wallet-link round trip. See <see cref="ExpiryWithin"/>.
	/// </para>
	/// </summary>
	public long? Expiry { get; set; }

	/// <summary>
	/// Default lifetime of a message a builder stamps, in milliseconds.
	/// <para>
	/// The chain reads <c>expiry</c> in milliseconds and refuses anything at or beyond
	/// <c>now + expiryWindow</c>. <c>expiryWindow</c> is a chain setting, and its node default is
	/// 60,000 ms.
	/// </para>
	/// <para>
	/// A default has to hold on the shortest window a chain may run. The value is also compared against
	/// the NODE's clock, so it has to survive the two clocks disagreeing. That is why it keeps a
	/// quarter of a minute of headroom and does not take the whole 60,000.
	/// </para>
	/// <para>
	/// A chain that allows longer reports its own window as <c>ExpiryWindow</c> in getGasConfig,
	/// reachable as <c>PhantasmaAPI.Fees.ChainParamsAsync()</c>.
	/// </para>
	/// </summary>
	public const long DefaultExpiryMs = 45_000;

	/// <summary>Expiry for a message built to be signed and sent now.</summary>
	public static long DefaultExpiry() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + DefaultExpiryMs;

	/// <summary>
	/// Returns the latest expiry a chain with this window will still admit, less a margin for the clock
	/// the node compares it against. Use it when a person sits between building a transaction and
	/// signing it. The chain's window is usually far longer than <see cref="DefaultExpiryMs"/>, and the
	/// whole of it is available.
	/// </summary>
	/// <param name="expiryWindowMs">The chain's window, from <c>PhantasmaAPI.Fees.ChainParamsAsync()</c>.</param>
	/// <param name="marginMs">Headroom for clock skew and the trip to the node. Default 5,000 ms.</param>
	public static long ExpiryWithin(long expiryWindowMs, long marginMs = 5_000)
	{
		if (expiryWindowMs <= 0)
			throw new ArgumentOutOfRangeException(nameof(expiryWindowMs), "expiryWindowMs must be positive");
		if (marginMs < 0)
			throw new ArgumentOutOfRangeException(nameof(marginMs), "marginMs must not be negative");
		var lifetime = expiryWindowMs - marginMs;
		if (lifetime <= 0)
			throw new ArgumentOutOfRangeException(nameof(marginMs), $"a margin of {marginMs}ms leaves nothing of a {expiryWindowMs}ms window");
		return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + lifetime;
	}

	/// <summary>Writes the limits into a message the builders assemble.</summary>
	internal static TxMsg Apply(TxMsg msg, TxLimits? limits)
	{
		msg.maxGas = limits?.MaxGas ?? 0;
		msg.maxData = limits?.MaxData ?? 0;
		msg.expiry = limits?.Expiry ?? DefaultExpiry();
		return msg;
	}
}

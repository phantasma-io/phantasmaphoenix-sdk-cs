namespace PhantasmaPhoenix.Protocol;

/// <summary>Settings of a <see cref="WalletLinkV5"/> that differ between wallet hosts.</summary>
public sealed class WalletLinkV5Options
{
	/// <summary>The transports the host serves. The capability handshake gives the size limit of
	/// these only (spec §11). Default: loopback, deeplink and relay.</summary>
	public LinkTransportKind[] ServedTransports { get; set; } =
	{
		LinkTransportKind.Loopback,
		LinkTransportKind.Deeplink,
		LinkTransportKind.Relay,
	};

	/// <summary>A session without a request for this long ends (spec §7). Default: 7 days.</summary>
	public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromDays(7);

	/// <summary>The most sessions the wallet keeps. A new session past it evicts the least recently
	/// used one, whose dApp gets <c>pha_sessionDeleted</c> (spec §7). Default: 50.</summary>
	public int MaxSessions { get; set; } = 50;
}

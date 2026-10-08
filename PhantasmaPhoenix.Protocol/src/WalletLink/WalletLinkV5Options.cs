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
}

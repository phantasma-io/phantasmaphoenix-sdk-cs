namespace PhantasmaPhoenix.Protocol;

// What a transport tells the v5 dispatcher about the channel a request came on (spec §6, §7).
// The dispatcher binds sessions to it and answers by its rules: the origin is checked against the
// session's, and the transport is shown to the user in every approval.

/// <summary>The Link transports of this SDK's wallet side (spec §6), named as in
/// <c>maxPayloadBytes</c> (spec §5).</summary>
public enum LinkTransportKind
{
	Loopback,
	Deeplink,
	Relay,
}

/// <summary>The channel of one request, given to <see cref="WalletLinkV5.HandleMessage"/>.</summary>
public sealed class LinkRequestContext
{
	/// <summary>The transport that carried the request.</summary>
	public LinkTransportKind Transport { get; set; } = LinkTransportKind.Loopback;

	/// <summary>The origin of the caller as the transport learned it, never from the request: the
	/// page origin of a loopback connection, or the dApp url of a pairing. The dApp url is what the
	/// dApp declared in the pairing; nothing verifies it (spec §7). Null when unknown; the approval
	/// then labels the caller as unverified (spec §6.2).</summary>
	public string? Origin { get; set; }

	/// <summary>Over deeplink and relay, the session that the pairing of the request carries, or
	/// null before its first session. A <c>pha_connect</c> without a session resumes it (spec §9.5).</summary>
	public string? PairingSession { get; set; }

	/// <summary>Names the channel the request came on, unique within the wallet: a pairing, or a
	/// loopback connection. A <c>pha_connect</c> that prompts has no session yet, so the wallet
	/// remembers its id with the channel and drops a copy (spec §18.2); over loopback the page origin
	/// names the channel when there is one.</summary>
	public string? Channel { get; set; }

	/// <summary>Called with the id of the session that the dispatcher accepted a request of, or
	/// created for it. A transport keeps it as the session its channel carries, to deliver that
	/// session's events and to end the channel with the session.</summary>
	public Action<string>? OnSession { get; set; }
}

/// <summary>Who sent a request, as every approval shows it (spec §6.2, §7, §9.5): the dApp, the
/// origin and the account of its session.</summary>
public sealed class LinkCaller
{
	public string DappName { get; set; } = "";
	public string DappUrl { get; set; } = "";
	/// <summary>The dApp icon URL, or empty.</summary>
	public string DappIcon { get; set; } = "";
	/// <summary>The origin the transport reported: over loopback the page origin; over deeplink and
	/// relay the dApp url that the pairing declared, which nothing verifies, so a prompt shows it as
	/// declared (spec §7). Null when it is unknown (an unverified caller).</summary>
	public string? Origin { get; set; }
	/// <summary>The account of the session the request belongs to; empty for a connect, where the
	/// user picks the account.</summary>
	public string Address { get; set; } = "";
	public LinkTransportKind Transport { get; set; }
}

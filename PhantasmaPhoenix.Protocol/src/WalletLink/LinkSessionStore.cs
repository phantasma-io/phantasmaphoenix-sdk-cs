namespace PhantasmaPhoenix.Protocol;

// Persistent session storage for Phantasma Link v5 (spec §7): sessions survive wallet restarts,
// so a returning dApp resumes with its known session id instead of re-prompting the user. The
// dispatcher owns the resume LOGIC; the wallet host owns the storage MEDIUM by implementing
// ILinkSessionStore (e.g. PlayerPrefs in a Unity wallet). InMemoryLinkSessionStore is the
// default for tests and for hosts that deliberately want non-persistent sessions.

/// <summary>How long a Link session lasts (spec §7), as the user picks it at the approval. Every
/// lifetime also ends by inactivity, eviction and revoke. The values are stored with the session:
/// <c>Always</c> is 0, so a session stored before lifetimes existed keeps no fixed end.</summary>
public enum LinkSessionLifetime
{
	/// <summary>Until the wallet locks; a restart locks the wallet. No fixed end date.</summary>
	Session = 1,
	/// <summary>One hour from the approval.</summary>
	OneHour = 2,
	/// <summary>One day from the approval.</summary>
	OneDay = 3,
	/// <summary>30 days from the approval.</summary>
	OneMonth = 4,
	/// <summary>No fixed end.</summary>
	Always = 0,
}

/// <summary>Why a Link session ended (spec §7). It picks the notice the dApp gets:
/// <c>pha_sessionExpired</c> for <see cref="Expired"/>, <c>pha_sessionDeleted</c> for the rest.</summary>
public enum LinkSessionEndReason
{
	/// <summary>Its lifetime or the inactivity limit ended it.</summary>
	Expired,
	/// <summary>The user revoked it in the wallet.</summary>
	Revoked,
	/// <summary>The dApp ended it with <c>pha_disconnect</c>.</summary>
	Disconnected,
	/// <summary>The wallet evicted it to stay within its limits.</summary>
	Evicted,
}

/// <summary>One authorized dApp session as the wallet remembers it.</summary>
public sealed class LinkSessionRecord
{
	public string Id { get; set; } = "";
	/// <summary>The dApp name the user approved. A session bound to no origin resumes for this
	/// name only (spec §7).</summary>
	public string Dapp { get; set; } = "";
	/// <summary>The dApp url the user approved (spec §7).</summary>
	public string DappUrl { get; set; } = "";
	/// <summary>The dApp icon URL, or empty.</summary>
	public string DappIcon { get; set; } = "";
	/// <summary>The origin the session is bound to (spec §6.2, §7), or null when the transport
	/// reported none. A request from another origin is refused.</summary>
	public string? Origin { get; set; }
	/// <summary>The transport the session was made over (spec §7): a session made over deeplink or
	/// relay is served over its pairing only. Null for a session stored before it was kept, which
	/// is served as one made over loopback.</summary>
	public LinkTransportKind? Transport { get; set; }
	/// <summary>The account the session was authorized for; resume requires the same account.</summary>
	public string Address { get; set; } = "";
	/// <summary>The networks granted to the session, as CAIP-2 ids (spec §5). Null for a session
	/// stored before the grants were kept: it gets every network the wallet serves.</summary>
	public string[]? Chains { get; set; }
	/// <summary>The methods granted to the session (spec §5). Null for a session stored before the
	/// grants were kept: it gets every method the wallet serves.</summary>
	public string[]? Methods { get; set; }
	/// <summary>The features granted to the session (spec §5); this version defines none.</summary>
	public string[]? Features { get; set; }
	/// <summary>The lifetime the user picked (spec §7).</summary>
	public LinkSessionLifetime Lifetime { get; set; } = LinkSessionLifetime.Always;
	/// <summary>The fixed end of the lifetime, or null when it has none (<c>Session</c> and
	/// <c>Always</c>).</summary>
	public DateTime? ExpiresUtc { get; set; }
	public DateTime CreatedUtc { get; set; }
	/// <summary>The last request in the session; the inactivity limit counts from it (spec §7).</summary>
	public DateTime LastSeenUtc { get; set; }
	/// <summary>The request ids the session handled within the replay window (spec §18.2). They are
	/// stored with the session, so that a copy of a request is dropped after a restart too.</summary>
	public List<LinkSeenRequest> SeenRequests { get; set; } = new List<LinkSeenRequest>();
}

/// <summary>A request id that a session handled, and until when the wallet keeps it (spec §18.2).</summary>
public sealed class LinkSeenRequest
{
	public string Id { get; set; } = "";
	public DateTime UntilUtc { get; set; }
}

/// <summary>
/// Wallet-side session storage. All calls happen on the wallet's dispatch thread (the same one
/// that runs <see cref="WalletLinkV5.HandleMessage"/>), so implementations need no locking. An
/// implementation keeps every property of a record: one it drops, such as
/// <see cref="LinkSessionRecord.Transport"/>, loosens what the wallet checks.
/// </summary>
public interface ILinkSessionStore
{
	LinkSessionRecord? Get(string id);
	void Save(LinkSessionRecord record);
	void Remove(string id);
	/// <summary>All stored sessions (for a future revoke/management UI).</summary>
	LinkSessionRecord[] List();
}

/// <summary>Non-persistent store: sessions die with the process.</summary>
public sealed class InMemoryLinkSessionStore : ILinkSessionStore
{
	private readonly Dictionary<string, LinkSessionRecord> _records = new Dictionary<string, LinkSessionRecord>();

	public LinkSessionRecord? Get(string id)
	{
		return _records.TryGetValue(id, out var record) ? record : null;
	}

	public void Save(LinkSessionRecord record)
	{
		_records[record.Id] = record;
	}

	public void Remove(string id)
	{
		_records.Remove(id);
	}

	public LinkSessionRecord[] List()
	{
		return _records.Values.ToArray();
	}
}

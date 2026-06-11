namespace PhantasmaPhoenix.Protocol;

// Persistent session storage for Phantasma Link v5 (spec §7): sessions survive wallet restarts,
// so a returning dApp resumes with its known session id instead of re-prompting the user. The
// dispatcher owns the resume LOGIC; the wallet host owns the storage MEDIUM by implementing
// ILinkSessionStore (e.g. PlayerPrefs in a Unity wallet). InMemoryLinkSessionStore is the
// default for tests and for hosts that deliberately want non-persistent sessions.

/// <summary>One authorized dApp session as the wallet remembers it.</summary>
public sealed class LinkSessionRecord
{
	public string Id { get; set; } = "";
	/// <summary>The dApp identity the user approved; resume requires an exact match.</summary>
	public string Dapp { get; set; } = "";
	/// <summary>The account the session was authorized for; resume requires the same account.</summary>
	public string Address { get; set; } = "";
	public DateTime CreatedUtc { get; set; }
	public DateTime LastSeenUtc { get; set; }
}

/// <summary>
/// Wallet-side session storage. All calls happen on the wallet's dispatch thread (the same one
/// that runs <see cref="WalletLinkV5.HandleMessage"/>), so implementations need no locking.
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

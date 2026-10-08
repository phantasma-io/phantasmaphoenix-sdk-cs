namespace PhantasmaPhoenix.Protocol;

// Persistent pairing storage for Phantasma Link v5 deeplink/relay channels (spec §15/§17).
// A pairing binds a topic to the channel session key and the dApp's response callback; it
// outlives wallet restarts so a paired dApp keeps working without re-pairing. Same layering
// as ILinkSessionStore: the dispatcher logic lives in the endpoint, the wallet host owns the
// storage medium.

/// <summary>One approved dApp pairing as the wallet remembers it.</summary>
public sealed class LinkPairingRecord
{
	public string Topic { get; set; } = "";
	/// <summary>The 32-byte channel session key (sym key, or the ECDH-derived key).</summary>
	public byte[] Key { get; set; } = Array.Empty<byte>();
	/// <summary>Where the wallet opens response deeplinks for this pairing.</summary>
	public string CallbackUrl { get; set; } = "";
	/// <summary>Relay WebSocket URL for this pairing (spec section 15); null/empty for a
	/// deeplink-only pairing. Normalized from the pairing URI's `relay` field.</summary>
	public string? RelayUrl { get; set; }
	public string DappName { get; set; } = "";
	/// <summary>The dApp url from the pairing's <c>meta</c> (spec §15). Requests of the pairing carry
	/// it as their origin.</summary>
	public string DappUrl { get; set; } = "";
	public DateTime CreatedUtc { get; set; }
	public DateTime LastSeenUtc { get; set; }
	/// <summary>The v5 session id (§7) this channel carries. The pairing ends with this session
	/// (spec §18.3), and the session's events go over this channel. Empty until the first session
	/// of the pairing.</summary>
	public string SessionId { get; set; } = "";

	/// <summary>Returns the context of a request that arrived over this pairing: its transport, the
	/// dApp url of the pairing as the origin, the session of the pairing, and the session the
	/// request binds, which the pairing then keeps in <paramref name="store"/>.</summary>
	internal LinkRequestContext RequestContext(LinkTransportKind transport, ILinkPairingStore store)
	{
		return new LinkRequestContext
		{
			Transport = transport,
			Origin = DappUrl.Length > 0 ? DappUrl : null,
			PairingSession = SessionId.Length > 0 ? SessionId : null,
			Channel = "pairing " + Topic,
			OnSession = session =>
			{
				// A pairing that ended while its request waited for the user stays ended: saving it
				// again would bring its key back (spec §18.3).
				if (SessionId != session && store.Get(Topic) != null)
				{
					SessionId = session;
					store.Save(this);
				}
			},
		};
	}
}

/// <summary>
/// Wallet-side pairing storage. All calls happen on the wallet's dispatch thread, so
/// implementations need no locking.
/// </summary>
public interface ILinkPairingStore
{
	LinkPairingRecord? Get(string topic);
	void Save(LinkPairingRecord record);
	void Remove(string topic);
	/// <summary>All stored pairings (for a future revoke/management UI).</summary>
	LinkPairingRecord[] List();
}

/// <summary>Non-persistent store: pairings die with the process.</summary>
public sealed class InMemoryLinkPairingStore : ILinkPairingStore
{
	private readonly Dictionary<string, LinkPairingRecord> _records = new Dictionary<string, LinkPairingRecord>();

	public LinkPairingRecord? Get(string topic)
	{
		return _records.TryGetValue(topic, out var record) ? record : null;
	}

	public void Save(LinkPairingRecord record)
	{
		_records[record.Topic] = record;
	}

	public void Remove(string topic)
	{
		_records.Remove(topic);
	}

	public LinkPairingRecord[] List()
	{
		return _records.Values.ToArray();
	}
}

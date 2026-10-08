using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PhantasmaPhoenix.Protocol;

/// <summary>
/// Minimal duplex text socket the relay client runs over. Implementations live next to
/// the host's networking stack (PhantasmaPhoenix.Link provides the real WebSocket one);
/// callbacks may fire on any thread.
/// </summary>
public interface ILinkRelaySocket
{
	void SendText(string text);
	void Close();
}

public interface ILinkRelaySocketFactory
{
	/// <summary>Open a socket to <paramref name="url"/>. <paramref name="onOpen"/> fires once
	/// the connection is usable, <paramref name="onText"/> per complete text message, and
	/// <paramref name="onClosed"/> exactly once when the connection dies (any reason).</summary>
	ILinkRelaySocket Connect(string url, Action onOpen, Action<string> onText, Action onClosed);
}

/// <summary>
/// Wallet-side client of the Phantasma Link relay (spec section 16). Keeps one outbound
/// connection per relay URL. A pairing uses two relay topics: the wallet subscribes to the
/// dApp's topic `&lt;t&gt;.dapp` and publishes to its own `&lt;t&gt;.wallet`, so it never receives its
/// own frames. It unseals delivered frames with the pairing key, routes them through
/// <see cref="WalletLinkV5"/>, and publishes the sealed responses back - chunking anything
/// above the relay frame cap. The relay stays E2E-blind: plaintext never leaves this class.
///
/// Threading: socket callbacks arrive on arbitrary threads; all dispatcher work is run
/// through the host-provided marshal hook (the wallet posts to its UI thread). Responses
/// are fire-and-forget (no resend tracking): a response lost to a connection drop is
/// recovered by the dApp re-sending its request, which the dispatcher answers again.
/// </summary>
public sealed class LinkRelayClient : IDisposable
{
	// Mirrors the TS transport: stay under the relay's 1 MiB frame cap with room for the
	// publish envelope; bound reassembly so a peer cannot balloon wallet memory.
	private const int ChunkChars = 900_000;

	/// <summary>Room for the envelope around the transaction: the method, the ids, the other
	/// params, and the signatures that a signed transaction adds.</summary>
	private const int EnvelopeAllowanceBytes = 64 * 1024;

	// The longest sealed frame (spec section 16), for a transaction at the chain maximum. It
	// holds two base64 layers: the transaction is base64 inside the envelope JSON, and the
	// sealed envelope (plus the 16-byte authenticator) is base64 again in the frame object,
	// which adds the nonce and the field names (128 characters cover them). Counting one
	// layer only gives about 30.9 MiB instead of 32 MiB.
	private const int MaxSealedFrameChars =
		((int)WalletLinkV5.MaxTxBase64Chars + EnvelopeAllowanceBytes + 16 + 2) / 3 * 4 + 128;

	// The chunk count is the same both ways: 67.
	private const int MaxChunksPerMessage = (MaxSealedFrameChars + ChunkChars - 1) / ChunkChars;
	private const int MaxAssembledChars = 64 * 1024 * 1024;
	private const int MaxConcurrentPartials = 8;
	private static readonly TimeSpan PartialStale = TimeSpan.FromSeconds(120);

	private sealed class Partial
	{
		public int Total;
		public Dictionary<int, string> Received = new Dictionary<int, string>();
		public int Chars;
		public DateTime TouchedUtc;
	}

	private sealed class Connection
	{
		public string Url = "";
		public ILinkRelaySocket? Socket;
		public bool Open;
		public readonly HashSet<string> Topics = new HashSet<string>();
		/// <summary>Frames queued while the socket is connecting/reconnecting.</summary>
		public readonly List<string> Outbox = new List<string>();
		public int ReconnectAttempt;
		public System.Threading.Timer? ReconnectTimer;
	}

	private readonly WalletLinkV5 _dispatcher;
	private readonly ILinkPairingStore _pairings;
	private readonly ILinkRelaySocketFactory _sockets;
	private readonly Action<Action> _marshal;
	private readonly int[] _reconnectDelaysMs;
	// Spec §7 session-store lifecycle: keep only the N most-recently-used relay sessions
	// subscribed (LRU eviction by LastSeenUtc), kept BELOW the relay's per-connection topic cap
	// (§16, default 8) so a fresh pairing always has a slot. Without this the (cap+1)-th
	// pairing's subscribe is rejected and that dApp hangs.
	private readonly int _relaySessionCap;
	private readonly object _gate = new object();
	private readonly Dictionary<string, Connection> _connections = new Dictionary<string, Connection>();
	private readonly Dictionary<string, Partial> _partials = new Dictionary<string, Partial>();
	private int _publishSeq;
	private bool _disposed;

	public LinkRelayClient(
		WalletLinkV5 dispatcher,
		ILinkPairingStore pairings,
		ILinkRelaySocketFactory sockets,
		Action<Action>? marshal = null,
		int[]? reconnectDelaysMs = null,
		Action<string>? log = null,
		int relaySessionCap = 6)
	{
		_dispatcher = dispatcher;
		_pairings = pairings;
		_sockets = sockets;
		// Default marshal runs inline; a Unity host MUST pass its post-to-UI-thread hook
		// because the dispatcher prompts the user and touches wallet state.
		_marshal = marshal ?? (action => action());
		_reconnectDelaysMs = reconnectDelaysMs ?? new[] { 1000, 2000, 5000, 15000, 30000 };
		// Default 6 sits below the relay's default 8-topic cap.
		_relaySessionCap = relaySessionCap > 0 ? relaySessionCap : 6;
		// Transport visibility: this client absorbs connection failures by silent
		// reconnection, so without a log sink a host cannot tell a dead relay link from
		// a quiet one. Hosts wire this to their own logger.
		_log = log;
		// The sessions of relay pairings send their events and their end through this client.
		_dispatcher.SessionEvent += OnSessionEvent;
		_dispatcher.SessionEnded += OnSessionEnded;
	}

	private readonly Action<string>? _log;

	/// <summary>Returns the relay topic that the dApp of a pairing publishes to and the wallet
	/// subscribes to: the pairing topic <c>t</c> and <c>.dapp</c> (spec section 16).</summary>
	public static string DappTopic(string pairingTopic) => pairingTopic + ".dapp";

	/// <summary>Returns the relay topic that the wallet publishes to and the dApp subscribes to:
	/// the pairing topic <c>t</c> and <c>.wallet</c> (spec section 16).</summary>
	public static string WalletTopic(string pairingTopic) => pairingTopic + ".wallet";

	/// <summary>Connect and reconcile subscriptions for every stored relay pairing (wallet start,
	/// /v5/wake). Ends the expired sessions and keeps only the most-recently-used pairings within
	/// the cap. Safe to call repeatedly: existing connections are reused.</summary>
	public void EnsureConnected()
	{
		Reconcile(null);
	}

	/// <summary>Track a just-saved pairing: ensure it is subscribed, evicting/pruning other relay
	/// sessions as needed to stay within the cap. The given pairing is always kept.</summary>
	public void TrackPairing(LinkPairingRecord pairing)
	{
		if (string.IsNullOrEmpty(pairing.RelayUrl))
		{
			return;
		}
		Reconcile(pairing.Topic);
	}

	/// <summary>Bring the subscribed relay sessions in line with the spec §7 lifecycle: end the
	/// expired sessions and the pairings that outlived theirs, keep the
	/// <see cref="_relaySessionCap"/> most-recently-used pairings (evict the rest, below the relay's
	/// per-connection topic cap), and subscribe the survivors. <paramref name="protectTopic"/> (a
	/// just-created pairing) is never evicted. Runs on the dispatch thread; the per-connection
	/// helpers take the gate themselves.</summary>
	private void Reconcile(string? protectTopic)
	{
		// 1) Sessions past their lifetime or the inactivity limit end with their notice, and their
		//    pairings with them (OnSessionEnded).
		_dispatcher.ExpireSessions();

		// 2) A pairing lives as long as its session (spec §18.3): one whose session the wallet no
		//    longer keeps ends, and one that never got a session ends when it stays idle past the
		//    inactivity limit. The just-created pairing is exempt.
		var now = DateTime.UtcNow;
		foreach (var p in RelayPairings().Where(p => p.Topic != protectTopic))
		{
			var ended = p.SessionId.Length > 0
				? !_dispatcher.IsSessionLive(p.SessionId)
				: now - p.LastSeenUtc >= _dispatcher.SessionIdleTimeout;
			if (ended)
			{
				Evict(p, "session ended");
			}
		}

		// 3) Most-recently-used first (the fresh protected topic forced to the front), then evict
		//    everything past the cap so the relay never rejects our subscribe with topic_limit.
		var ordered = RelayPairings()
			.OrderByDescending(p => p.Topic == protectTopic)
			.ThenByDescending(p => p.LastSeenUtc)
			.ToList();
		foreach (var p in ordered.Skip(_relaySessionCap).ToArray())
		{
			Evict(p, "lru");
		}

		// 4) Subscribe the survivors (idempotent).
		foreach (var p in RelayPairings())
		{
			SubscribeTopic(p);
		}
	}

	private List<LinkPairingRecord> RelayPairings()
	{
		return _pairings.List().Where(p => !string.IsNullOrEmpty(p.RelayUrl)).ToList();
	}

	/// <summary>Ends a relay pairing. One with a session ends through the session (spec §7): its
	/// dApp gets <c>pha_sessionDeleted</c> and every transport ends its channel.</summary>
	private void Evict(LinkPairingRecord pairing, string reason)
	{
		_log?.Invoke($"evict pairing reason={reason}");
		if (pairing.SessionId.Length > 0)
		{
			_dispatcher.EndSession(pairing.SessionId, LinkSessionEndReason.Evicted);
		}
		ForgetPairing(pairing);
	}

	/// <summary>Delivers an event of a session to the dApp of its relay pairing (spec §9.5).</summary>
	private void OnSessionEvent(string session, string envelope)
	{
		foreach (var pairing in RelayPairings())
		{
			if (pairing.SessionId == session)
			{
				PublishSealed(pairing, envelope);
			}
		}
	}

	/// <summary>A relay pairing ends with its session (spec §7, §18.3): the wallet leaves its topic
	/// and forgets its key. The session's notice went out before.</summary>
	private void OnSessionEnded(string session, LinkSessionEndReason reason)
	{
		foreach (var pairing in RelayPairings())
		{
			if (pairing.SessionId == session)
			{
				_log?.Invoke($"pairing ended with its session reason={reason}");
				ForgetPairing(pairing);
			}
		}
	}

	/// <summary>Leaves the relay topic of a pairing and forgets the pairing with its key.</summary>
	private void ForgetPairing(LinkPairingRecord pairing)
	{
		UnsubscribeRelay(pairing);
		_pairings.Remove(pairing.Topic);
	}

	/// <summary>Subscribe one pairing's topic on its relay connection (connecting if needed).
	/// Idempotent: a topic already in the connection's set is not re-announced.</summary>
	private void SubscribeTopic(LinkPairingRecord pairing)
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}
			var conn = GetOrCreateConnection(pairing.RelayUrl!);
			var relayTopic = DappTopic(pairing.Topic);
			if (!conn.Topics.Add(relayTopic))
			{
				return; // already subscribed
			}
			// Only announce on a LIVE socket: OnOpen announces every topic in Topics, so
			// queueing here would double-subscribe after the connect completes.
			var live = conn.Open && conn.Socket != null;
			_log?.Invoke($"subscribe topic={pairing.Topic} live={live}");
			if (live)
			{
				conn.Socket!.SendText(new JObject { ["op"] = "subscribe", ["topic"] = relayTopic }.ToString(Formatting.None));
			}
		}
	}

	/// <summary>Unsubscribe a topic on its relay connection and drop it from the tracked set.</summary>
	private void UnsubscribeRelay(LinkPairingRecord pairing)
	{
		// A deeplink-only pairing carries no relay URL and never subscribed a relay topic, so there
		// is nothing to unsubscribe. Guard before the lookup: Dictionary.TryGetValue throws
		// ArgumentNullException on a null key.
		if (string.IsNullOrEmpty(pairing.RelayUrl))
		{
			return;
		}
		lock (_gate)
		{
			if (_disposed || !_connections.TryGetValue(pairing.RelayUrl!, out var conn))
			{
				return;
			}
			var relayTopic = DappTopic(pairing.Topic);
			if (conn.Topics.Remove(relayTopic) && conn.Open && conn.Socket != null)
			{
				conn.Socket.SendText(new JObject { ["op"] = "unsubscribe", ["topic"] = relayTopic }.ToString(Formatting.None));
			}
		}
	}

	/// <summary>Seal one envelope with the pairing key and publish it to the pairing's
	/// topic, chunking when it exceeds the relay frame budget.</summary>
	public void PublishSealed(LinkPairingRecord pairing, string envelopeJson)
	{
		if (string.IsNullOrEmpty(pairing.RelayUrl))
		{
			return;
		}
		var channel = new LinkChannel(pairing.Key);
		var frame = channel.SealEnvelope(envelopeJson);
		if ((frame.Length + ChunkChars - 1) / ChunkChars > MaxChunksPerMessage)
		{
			// Spec section 11: a payload above the budget gets a clear 5001 with the request id.
			// Without it the dApp would wait for its own timeout. An event has no id and nobody
			// waits for an answer to it, so an oversized event is only logged.
			var id = ReadEnvelopeId(envelopeJson);
			if (id == null)
			{
				_log?.Invoke($"publish DROP topic={pairing.Topic}: {frame.Length} chars exceed the relay budget");
				return;
			}
			frame = channel.SealEnvelope(_dispatcher.BuildPayloadTooLargeEnvelope(id, WalletLinkV5.MaxTxBytes));
		}
		_log?.Invoke($"publish sealed topic={pairing.Topic} bytes={frame.Length}");

		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}
			var conn = GetOrCreateConnection(pairing.RelayUrl!);
			if (frame.Length <= ChunkChars)
			{
				SendOrQueue(conn, BuildPublish(WalletTopic(pairing.Topic), frame));
				return;
			}
			// Chunked publish (spec section 18): split the sealed frame TEXT; only the
			// receiving SDK reassembles - the relay forwards opaque pieces.
			var total = (frame.Length + ChunkChars - 1) / ChunkChars;
			var msgId = Guid.NewGuid().ToString("N");
			for (var seq = 0; seq < total; seq++)
			{
				var start = seq * ChunkChars;
				var chunk = frame.Substring(start, Math.Min(ChunkChars, frame.Length - start));
				var payload = new JObject
				{
					["msgId"] = msgId,
					["seq"] = seq,
					["total"] = total,
					["chunk"] = chunk,
				};
				SendOrQueue(conn, BuildPublish(WalletTopic(pairing.Topic), payload));
			}
		}
	}

	/// <summary>ecdh pairing key hop (spec section 20.1): publish the wallet's ephemeral
	/// X25519 PUBLIC key together with the first sealed envelope (the connect result) in
	/// one payload, so the dApp can derive the channel key and immediately open it. Sent
	/// once per pairing; small by construction, so it is never chunked.</summary>
	public void PublishHandshake(LinkPairingRecord pairing, byte[] walletPublicKey, string envelopeJson)
	{
		if (string.IsNullOrEmpty(pairing.RelayUrl))
		{
			return;
		}
		var channel = new LinkChannel(pairing.Key);
		// SealEnvelope yields the {"nonce","ct"} frame; the public key rides beside the
		// sealed fields IN THE CLEAR - it is the very material the peer needs to decrypt.
		var payload = JObject.Parse(channel.SealEnvelope(envelopeJson));
		payload["wpk"] = LinkEncoding.Base64UrlEncode(walletPublicKey);
		_log?.Invoke($"publish handshake topic={pairing.Topic} (one-tap session push)");

		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}
			var conn = GetOrCreateConnection(pairing.RelayUrl!);
			SendOrQueue(conn, BuildPublish(WalletTopic(pairing.Topic), payload));
		}
	}

	public void Dispose()
	{
		_dispatcher.SessionEvent -= OnSessionEvent;
		_dispatcher.SessionEnded -= OnSessionEnded;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}
			_disposed = true;
			foreach (var conn in _connections.Values)
			{
				conn.ReconnectTimer?.Dispose();
				conn.Socket?.Close();
			}
			_connections.Clear();
			_partials.Clear();
		}
	}

	// --- connection lifecycle (all called under _gate unless noted) ---------------------

	private Connection GetOrCreateConnection(string url)
	{
		if (_connections.TryGetValue(url, out var existing))
		{
			return existing;
		}
		var conn = new Connection { Url = url };
		_connections[url] = conn;
		Dial(conn);
		return conn;
	}

	private void Dial(Connection conn)
	{
		// The factory must not block (hosts call this from their UI thread); its
		// callbacks re-enter this class on arbitrary threads, hence the gate.
		conn.Socket = _sockets.Connect(
			conn.Url,
			onOpen: () => OnOpen(conn),
			onText: text => OnText(conn, text),
			onClosed: () => OnClosed(conn));
	}

	private void OnOpen(Connection conn)
	{
		_log?.Invoke($"relay connected: {conn.Url}");
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}
			conn.Open = true;
			conn.ReconnectAttempt = 0;
			// Re-announce every topic, then flush frames queued while offline. The relay
			// mailbox (TTL) covers what the other side published in between.
			foreach (var topic in conn.Topics)
			{
				conn.Socket?.SendText(new JObject { ["op"] = "subscribe", ["topic"] = topic }.ToString(Formatting.None));
			}
			foreach (var text in conn.Outbox)
			{
				conn.Socket?.SendText(text);
			}
			conn.Outbox.Clear();
		}
	}

	private void OnClosed(Connection conn)
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}
			conn.Open = false;
			conn.Socket = null;
			var delay = _reconnectDelaysMs[Math.Min(conn.ReconnectAttempt, _reconnectDelaysMs.Length - 1)];
			conn.ReconnectAttempt++;
			_log?.Invoke($"relay disconnected: {conn.Url}; reconnect in {delay} ms (attempt {conn.ReconnectAttempt})");
			conn.ReconnectTimer?.Dispose();
			// One-shot timer; the next drop schedules the next attempt (ladder backoff).
			conn.ReconnectTimer = new System.Threading.Timer(_ =>
			{
				lock (_gate)
				{
					if (!_disposed && !conn.Open)
					{
						Dial(conn);
					}
				}
			}, null, delay, System.Threading.Timeout.Infinite);
		}
	}

	private void SendOrQueue(Connection conn, string text)
	{
		if (conn.Open && conn.Socket != null)
		{
			conn.Socket.SendText(text);
		}
		else
		{
			conn.Outbox.Add(text);
		}
	}

	private string BuildPublish(string topic, JToken payload)
	{
		var id = "w" + System.Threading.Interlocked.Increment(ref _publishSeq);
		return new JObject
		{
			["op"] = "publish",
			["topic"] = topic,
			["id"] = id,
			["payload"] = payload,
		}.ToString(Formatting.None);
	}

	// --- incoming ------------------------------------------------------------------------

	/// <summary>Returns the text of a JSON string field, or null for a missing field or any other
	/// JSON type. A frame comes from the network: a cast of a field of another type would throw
	/// inside the socket callback, and the connection would be taken for dead.</summary>
	private static string? TextOf(JToken? token)
	{
		return token != null && token.Type == JTokenType.String ? (string?)token : null;
	}

	/// <summary>Returns the value of a JSON integer field that fits an int, or null. A JSON integer
	/// can be far larger than an int, and narrowing it would throw.</summary>
	private static int? IntOf(JToken? token)
	{
		if (token == null || token.Type != JTokenType.Integer)
		{
			return null;
		}
		return int.TryParse(token.ToString(Formatting.None), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : (int?)null;
	}

	private void OnText(Connection conn, string text)
	{
		JObject frame;
		try
		{
			frame = JObject.Parse(text);
		}
		catch
		{
			return; // not a relay frame
		}

		var op = TextOf(frame["op"]);
		// Surface relay errors instead of swallowing them (spec §16): a refused subscribe
		// (topic_limit) MUST be visible - silently dropping it makes the wallet believe it is
		// listening while the relay routes nothing to it, so every request on that topic hangs.
		if (op == "error")
		{
			_log?.Invoke($"relay error: code={TextOf(frame["code"])} {TextOf(frame["message"])} topic={TextOf(frame["topic"])}");
			return;
		}
		// acks confirm our own publishes; only deliver frames carry dApp requests.
		if (op != "deliver")
		{
			return;
		}
		// The wallet subscribes only to dApp topics, so a frame on any other topic is not for it.
		var relayTopic = TextOf(frame["topic"]);
		if (relayTopic == null || !relayTopic.EndsWith(".dapp", StringComparison.Ordinal))
		{
			return;
		}
		lock (_gate)
		{
			if (!conn.Topics.Contains(relayTopic))
			{
				return;
			}
		}
		var topic = relayTopic.Substring(0, relayTopic.Length - ".dapp".Length);
		_log?.Invoke($"deliver recv topic={topic}");

		// Chunk reassembly only needs the gate; everything touching the pairing store or
		// the dispatcher must run on the wallet's dispatch thread (the store contract is
		// single-threaded, and PlayerPrefs-backed stores are main-thread only in Unity).
		string? frameText = null;
		var payload = frame["payload"];
		if (payload != null && payload.Type == JTokenType.String)
		{
			frameText = (string?)payload;
		}
		else if (payload is JObject chunk)
		{
			lock (_gate)
			{
				frameText = AcceptChunk(topic!, chunk);
			}
		}
		if (frameText == null)
		{
			return;
		}

		var sealedFrame = frameText;
		_marshal(() => ProcessSealedFrame(topic!, sealedFrame));
	}

	/// <summary>Runs on the dispatch thread: resolve the pairing, unseal, dispatch, answer.</summary>
	private void ProcessSealedFrame(string topic, string frameText)
	{
		var pairing = _pairings.Get(topic);
		if (pairing == null)
		{
			_log?.Invoke($"deliver DROP topic={topic}: no pairing in store");
			return; // unknown channel; no key, nothing to say
		}
		var channel = new LinkChannel(pairing.Key);
		if (!channel.TryOpenEnvelope(frameText, out var envelopeJson))
		{
			_log?.Invoke($"deliver DROP topic={topic}: unseal failed (wrong key/forged)");
			return; // forged or wrong-key frame: drop silently, never answer plaintext
		}
		_log?.Invoke($"deliver OK topic={topic}: dispatching to wallet");
		pairing.LastSeenUtc = DateTime.UtcNow;
		_pairings.Save(pairing);
		_dispatcher.HandleMessage(envelopeJson, response => PublishSealed(pairing, response),
			pairing.RequestContext(LinkTransportKind.Relay, _pairings));
	}

	/// <summary>Returns the top-level <c>id</c> of an envelope, or null when it has none (an
	/// event). It reads the text as a stream and stops at the id, so a huge result is never
	/// built into objects.</summary>
	private static JToken? ReadEnvelopeId(string envelopeJson)
	{
		using var reader = new JsonTextReader(new StringReader(envelopeJson));
		while (reader.Read())
		{
			if (reader.TokenType == JsonToken.PropertyName && reader.Depth == 1 && (string?)reader.Value == "id")
			{
				reader.Read();
				return JToken.ReadFrom(reader);
			}
		}
		return null;
	}

	/// <summary>Collect one chunk (called under the gate); returns the reassembled frame
	/// text when complete. Bounds mirror the TS transport: chunk count, total size,
	/// concurrent partials, and staleness GC.</summary>
	private string? AcceptChunk(string topic, JObject raw)
	{
		var msgId = TextOf(raw["msgId"]);
		var chunk = TextOf(raw["chunk"]);
		var seqValue = IntOf(raw["seq"]);
		var totalValue = IntOf(raw["total"]);
		if (msgId == null || chunk == null || seqValue == null || totalValue == null)
		{
			return null;
		}
		var seq = seqValue.Value;
		var total = totalValue.Value;
		if (total < 1 || total > MaxChunksPerMessage || seq < 0 || seq >= total)
		{
			return null;
		}

		var now = DateTime.UtcNow;
		// Lazy staleness GC: an abandoned partial cannot linger past its window.
		var stale = _partials.Where(p => now - p.Value.TouchedUtc > PartialStale).Select(p => p.Key).ToArray();
		foreach (var key in stale)
		{
			_partials.Remove(key);
		}

		var partialKey = topic + "\n" + msgId;
		if (!_partials.TryGetValue(partialKey, out var partial))
		{
			if (_partials.Count >= MaxConcurrentPartials)
			{
				return null; // refuse new assemblies rather than grow without bound
			}
			partial = new Partial { Total = total, TouchedUtc = now };
			_partials[partialKey] = partial;
		}
		if (partial.Total != total || partial.Received.ContainsKey(seq))
		{
			return null; // inconsistent or duplicate chunk
		}
		partial.Chars += chunk.Length;
		if (partial.Chars > MaxAssembledChars)
		{
			_partials.Remove(partialKey);
			return null;
		}
		partial.Received[seq] = chunk;
		partial.TouchedUtc = now;

		if (partial.Received.Count != total)
		{
			return null;
		}
		_partials.Remove(partialKey);
		var builder = new System.Text.StringBuilder(partial.Chars);
		for (var i = 0; i < total; i++)
		{
			builder.Append(partial.Received[i]);
		}
		return builder.ToString();
	}
}

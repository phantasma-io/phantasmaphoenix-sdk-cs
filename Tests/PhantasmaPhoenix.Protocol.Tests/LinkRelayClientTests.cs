using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Tests;

/// <summary>
/// Wallet-side relay client (spec section 18): pairing-driven subscriptions, the one-tap
/// sessionEstablished push over the relay, sealed request/response round-trips, chunking
/// in both directions, and reconnect behavior - all over a scripted fake socket.
/// </summary>
public class LinkRelayClientTests
{
	private sealed class FakeRelaySocket : ILinkRelaySocket
	{
		public string Url = "";
		public readonly List<string> Sent = new List<string>();
		public Action? OnOpen;
		public Action<string>? OnText;
		public Action? OnClosed;
		public bool Closed;

		public void SendText(string text) => Sent.Add(text);
		public void Close() => Closed = true;

		public void Open() => OnOpen?.Invoke();
		public void Deliver(string text) => OnText?.Invoke(text);
		public void Drop() => OnClosed?.Invoke();
		public JObject[] SentFrames() => Sent.Select(JObject.Parse).ToArray();
	}

	private sealed class FakeRelaySocketFactory : ILinkRelaySocketFactory
	{
		public readonly List<FakeRelaySocket> Sockets = new List<FakeRelaySocket>();

		public ILinkRelaySocket Connect(string url, Action onOpen, Action<string> onText, Action onClosed)
		{
			var socket = new FakeRelaySocket { Url = url, OnOpen = onOpen, OnText = onText, OnClosed = onClosed };
			Sockets.Add(socket);
			return socket;
		}
	}

	private static (LinkDeeplinkEndpoint endpoint, FakeV5Ops ops, ILinkPairingStore pairings, LinkRelayClient relay, FakeRelaySocketFactory sockets) Build(int[]? ladder = null, int cap = 6)
	{
		var ops = new FakeV5Ops();
		var dispatcher = new WalletLinkV5(ops);
		var pairings = new InMemoryLinkPairingStore();
		var sockets = new FakeRelaySocketFactory();
		// Inline marshal: tests run single-threaded, the Unity host passes PostToUi instead.
		var relay = new LinkRelayClient(dispatcher, pairings, sockets, action => action(), ladder, null, cap);
		var endpoint = new LinkDeeplinkEndpoint(dispatcher, ops, pairings, relay);
		return (endpoint, ops, pairings, relay, sockets);
	}

	/// <summary>A relay pairing approved with dApp meta, its socket open and its session pushed.</summary>
	private sealed class PairedRelay
	{
		public FakeV5Ops Ops = null!;
		public WalletLinkV5 Dispatcher = null!;
		public InMemoryLinkSessionStore Sessions = null!;
		public ILinkPairingStore Pairings = null!;
		public LinkRelayClient Relay = null!;
		public FakeRelaySocket Socket = null!;
		public LinkChannel Channel = null!;
		public string Topic = "";
		public string SessionId = "";

		/// <summary>Delivers a sealed request from the dApp and returns the frames the wallet sent
		/// for it.</summary>
		public JObject[] Request(string envelope)
		{
			var before = Socket.Sent.Count;
			Socket.Deliver(new JObject { ["op"] = "deliver", ["topic"] = Topic + ".dapp", ["payload"] = Channel.SealEnvelope(envelope) }.ToString());
			return Socket.SentFrames().Skip(before).ToArray();
		}

		/// <summary>Returns the envelopes of the publish frames among <paramref name="frames"/>.</summary>
		public JObject[] Published(IEnumerable<JObject> frames)
		{
			return frames.Where(f => (string?)f["op"] == "publish").Select(f => OpenSealedPayload(Channel, f["payload"]!)).ToArray();
		}
	}

	private static PairedRelay PairOverRelay(string label)
	{
		var ops = new FakeV5Ops();
		var sessions = new InMemoryLinkSessionStore();
		var dispatcher = new WalletLinkV5(ops, sessions);
		var pairings = new InMemoryLinkPairingStore();
		var sockets = new FakeRelaySocketFactory();
		var relay = new LinkRelayClient(dispatcher, pairings, sockets, action => action());
		var endpoint = new LinkDeeplinkEndpoint(dispatcher, ops, pairings, relay);
		var key = new byte[32];
		key[9] = 1;
		var channel = new LinkChannel(key);
		var topic = LinkTestTopics.Of(label);
		endpoint.TryHandle(RelayPairingUri(key, topic, Relay, label + "-dapp"), _ => { }).ShouldBeTrue();
		var socket = sockets.Sockets[0];
		socket.Open();
		var sessionId = (string)OpenSealedPayload(channel, socket.SentFrames()[1]["payload"]!)["data"]!["session"]!["id"]!;
		return new PairedRelay
		{
			Ops = ops,
			Dispatcher = dispatcher,
			Sessions = sessions,
			Pairings = pairings,
			Relay = relay,
			Socket = socket,
			Channel = channel,
			Topic = topic,
			SessionId = sessionId,
		};
	}

	// Spec §15: a pairing names a wss:// relay. The fake socket factory never connects to it.
	private const string Relay = "wss://relay.test/relay";

	private static LinkPairingRecord RelayRecord(string topic, DateTime lastSeenUtc)
	{
		return new LinkPairingRecord
		{
			Topic = topic,
			Key = new byte[32],
			RelayUrl = Relay,
			DappName = "d",
			CreatedUtc = lastSeenUtc,
			LastSeenUtc = lastSeenUtc,
		};
	}

	private static string RelayPairingUri(byte[] key, string topic, string relay, string? dappName)
	{
		var sk = Convert.ToBase64String(key).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		var uri = $"https://link.phantasma.info/v5/pair#v=5&t={topic}&relay={Uri.EscapeDataString(relay)}&sk={sk}";
		if (dappName != null)
		{
			var metaJson = $"{{\"name\":\"{dappName}\",\"url\":\"https://dapp.example\"}}";
			var meta = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(metaJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
			uri += $"&meta={meta}";
		}
		return uri;
	}

	private static JObject OpenSealedPayload(LinkChannel channel, JToken payload)
	{
		channel.TryOpenEnvelope((string)payload!, out var envelopeJson).ShouldBeTrue();
		return JObject.Parse(envelopeJson);
	}

	// A relay frame comes from the network. A field of an unexpected JSON type, or a chunk number
	// far beyond an int, must be dropped quietly: a throw inside the socket callback would make the
	// host take the connection for dead and reconnect, and the relay would hand the same frame back
	// for its whole hold time.
	[Fact]
	public void Frames_with_fields_of_the_wrong_type_are_dropped_and_the_link_keeps_serving()
	{
		var (endpoint, _, _, _, sockets) = Build();
		var key = new byte[32];
		key[2] = 4;
		var channel = new LinkChannel(key);
		endpoint.TryHandle(RelayPairingUri(key, LinkTestTopics.Of("top-types"), Relay, "types-dapp"), _ => { }).ShouldBeTrue();
		var socket = sockets.Sockets[0];
		socket.Open();
		var sessionId = (string)OpenSealedPayload(channel, socket.SentFrames()[1]["payload"]!)["data"]!["session"]!["id"]!;
		var dappTopic = LinkTestTopics.Of("top-types") + ".dapp";
		var before = socket.SentFrames().Length;

		socket.Deliver("{\"op\":5}");
		socket.Deliver(new JObject { ["op"] = "deliver", ["topic"] = new JObject(), ["payload"] = "x" }.ToString());
		socket.Deliver(new JObject { ["op"] = "error", ["code"] = new JArray(), ["message"] = 7 }.ToString());
		socket.Deliver(new JObject
		{
			["op"] = "deliver",
			["topic"] = dappTopic,
			["payload"] = new JObject { ["msgId"] = new JObject(), ["seq"] = 0, ["total"] = 1, ["chunk"] = "x" },
		}.ToString());
		socket.Deliver("{\"op\":\"deliver\",\"topic\":\"" + dappTopic + "\",\"payload\":{\"msgId\":\"m\",\"seq\":100000000000000000000,\"total\":2,\"chunk\":\"x\"}}");
		socket.SentFrames().Length.ShouldBe(before);

		var request = $"{{\"plv\":5,\"id\":\"r2\",\"session\":\"{sessionId}\",\"method\":\"pha_getChains\"}}";
		socket.Deliver(new JObject { ["op"] = "deliver", ["topic"] = dappTopic, ["payload"] = channel.SealEnvelope(request) }.ToString());
		((string)OpenSealedPayload(channel, socket.SentFrames()[before]["payload"]!)["id"]!).ShouldBe("r2");
	}

	[Fact]
	public void Relay_pairing_pushes_the_session_and_serves_sealed_requests()
	{
		var (endpoint, ops, _, _, sockets) = Build();
		var key = new byte[32];
		key[7] = 5;
		var channel = new LinkChannel(key);

		// Cross-device shape: relay, dApp name, NO callback (a QR pairing has no usable
		// callback on the wallet's device).
		endpoint.TryHandle(RelayPairingUri(key, LinkTestTopics.Of("top-r"), Relay, "qr-dapp"), _ => { }).ShouldBeTrue();
		sockets.Sockets.Count.ShouldBe(1);
		sockets.Sockets[0].Url.ShouldBe(Relay);

		var socket = sockets.Sockets[0];
		socket.Open();
		var frames = socket.SentFrames();
		// First the topic subscription, then the one-tap sessionEstablished publish.
		((string)frames[0]["op"]!).ShouldBe("subscribe");
		((string)frames[0]["topic"]!).ShouldBe(LinkTestTopics.Of("top-r") + ".dapp");
		((string)frames[1]["op"]!).ShouldBe("publish");
		// Spec §16: the wallet publishes on its own topic of the pairing, never on the dApp's.
		((string)frames[1]["topic"]!).ShouldBe(LinkTestTopics.Of("top-r") + ".wallet");
		var pushed = OpenSealedPayload(channel, frames[1]["payload"]!);
		((string)pushed["event"]!).ShouldBe(WalletLinkV5.SessionEstablishedEvent);
		var sessionId = (string)pushed["data"]!["session"]!["id"]!;
		sessionId.ShouldNotBeNullOrEmpty();
		ops.ConnectCalls.ShouldBe(0); // the pairing consent was the only consent

		// The pushed session authorizes a sealed request delivered over the relay.
		var request = $"{{\"plv\":5,\"id\":\"r1\",\"session\":\"{sessionId}\",\"method\":\"pha_getChains\"}}";
		socket.Deliver(new JObject
		{
			["op"] = "deliver",
			["topic"] = LinkTestTopics.Of("top-r") + ".dapp",
			["payload"] = channel.SealEnvelope(request),
		}.ToString());

		var response = OpenSealedPayload(channel, socket.SentFrames()[2]["payload"]!);
		((string)response["id"]!).ShouldBe("r1");
		((string)response["result"]!["nexus"]!).ShouldBe("localnet");
		((string)socket.SentFrames()[2]["topic"]!).ShouldBe(LinkTestTopics.Of("top-r") + ".wallet");

		// A frame on the wallet's own topic, or on the bare pairing topic, is not a request for
		// the wallet: no answer goes out.
		var before = socket.SentFrames().Length;
		foreach (var topic in new[] { LinkTestTopics.Of("top-r") + ".wallet", LinkTestTopics.Of("top-r") })
		{
			socket.Deliver(new JObject
			{
				["op"] = "deliver",
				["topic"] = topic,
				["payload"] = channel.SealEnvelope($"{{\"plv\":5,\"id\":\"{topic}\",\"session\":\"{sessionId}\",\"method\":\"pha_getChains\"}}"),
			}.ToString());
		}
		socket.SentFrames().Length.ShouldBe(before);
	}

	[Fact]
	public void Chunked_requests_reassemble_and_large_responses_chunk()
	{
		var (endpoint, ops, _, _, sockets) = Build();
		// Make the wallet's answer far exceed the 900k chunk budget so the response MUST
		// leave as a chunked publish series.
		ops.InvokeResults = new[] { new byte[1_500_000] };
		var key = new byte[32];
		key[9] = 9;
		var channel = new LinkChannel(key);

		endpoint.TryHandle(RelayPairingUri(key, LinkTestTopics.Of("top-big"), Relay, "big-dapp"), _ => { }).ShouldBeTrue();
		var socket = sockets.Sockets[0];
		socket.Open();
		var sessionId = (string)OpenSealedPayload(channel, socket.SentFrames()[1]["payload"]!)["data"]!["session"]!["id"]!;

		// Deliver the request itself in two chunks, out of order, to prove reassembly.
		var request = $"{{\"plv\":5,\"id\":\"r2\",\"session\":\"{sessionId}\",\"method\":\"pha_invokeScript\",\"params\":{{\"chain\":\"main\",\"script\":\"QUJD\"}}}}";
		var sealedRequest = channel.SealEnvelope(request);
		var half = sealedRequest.Length / 2;
		JObject Chunk(int seq, string chunk) => new JObject
		{
			["op"] = "deliver",
			["topic"] = LinkTestTopics.Of("top-big") + ".dapp",
			["payload"] = new JObject { ["msgId"] = "m1", ["seq"] = seq, ["total"] = 2, ["chunk"] = chunk },
		};
		socket.Deliver(Chunk(1, sealedRequest.Substring(half)).ToString());
		socket.Deliver(Chunk(0, sealedRequest.Substring(0, half)).ToString());

		// Everything after subscribe + push must be the chunked response series.
		var frames = socket.SentFrames().Skip(2).ToArray();
		frames.Length.ShouldBeGreaterThan(1);
		var first = (JObject)frames[0]["payload"]!;
		var total = (int)first["total"]!;
		total.ShouldBe(frames.Length);
		var msgId = (string)first["msgId"]!;
		var assembled = new string[total];
		foreach (var frame in frames)
		{
			var payload = (JObject)frame["payload"]!;
			((string)payload["msgId"]!).ShouldBe(msgId);
			assembled[(int)payload["seq"]!] = (string)payload["chunk"]!;
		}
		var response = OpenSealedPayload(channel, string.Concat(assembled));
		((string)response["id"]!).ShouldBe("r2");
		((string)response["result"]!["results"]![0]!).ShouldBe(Convert.ToBase64String(ops.InvokeResults[0]));
	}

	[Fact]
	public void A_request_of_67_chunks_reassembles_and_one_of_68_is_dropped()
	{
		// Spec section 16: a receiver accepts up to 67 chunks, the sealed frame of a 32 MiB
		// transaction. A message that claims more is dropped and gets no answer.
		var (endpoint, _, _, _, sockets) = Build();
		var key = new byte[32];
		key[3] = 3;
		var channel = new LinkChannel(key);
		endpoint.TryHandle(RelayPairingUri(key, LinkTestTopics.Of("top-67"), Relay, "dapp-67"), _ => { }).ShouldBeTrue();
		var socket = sockets.Sockets[0];
		socket.Open();
		var sessionId = (string)OpenSealedPayload(channel, socket.SentFrames()[1]["payload"]!)["data"]!["session"]!["id"]!;

		void DeliverInChunks(string id, int total)
		{
			var sealedRequest = channel.SealEnvelope($"{{\"plv\":5,\"id\":\"{id}\",\"session\":\"{sessionId}\",\"method\":\"pha_getChains\"}}");
			for (var seq = 0; seq < total; seq++)
			{
				// An even split, so every chunk holds at least one character.
				var start = seq * sealedRequest.Length / total;
				var end = (seq + 1) * sealedRequest.Length / total;
				socket.Deliver(new JObject
				{
					["op"] = "deliver",
					["topic"] = LinkTestTopics.Of("top-67") + ".dapp",
					["payload"] = new JObject { ["msgId"] = id, ["seq"] = seq, ["total"] = total, ["chunk"] = sealedRequest.Substring(start, end - start) },
				}.ToString());
			}
		}

		DeliverInChunks("r67", 67);
		var answers = socket.SentFrames().Skip(2).ToArray();
		answers.Length.ShouldBe(1);
		((string)OpenSealedPayload(channel, answers[0]["payload"]!)["id"]!).ShouldBe("r67");

		DeliverInChunks("r68", 68);
		socket.SentFrames().Skip(2).Count().ShouldBe(1);
	}

	[Fact]
	public void A_response_for_a_32_MiB_transaction_leaves_in_67_chunks()
	{
		// The answer to a signing request for a 32 MiB transaction carries the signed
		// transaction as base64. Its sealed frame is 67 chunks, and every one goes out.
		var (endpoint, _, pairings, relay, sockets) = Build();
		var key = new byte[32];
		var channel = new LinkChannel(key);
		endpoint.TryHandle(RelayPairingUri(key, LinkTestTopics.Of("top-max"), Relay, "dapp-max"), _ => { }).ShouldBeTrue();
		var socket = sockets.Sockets[0];
		socket.Open();

		var signedTx = new string('A', 44_739_244); // the base64 length of 32 MiB
		relay.PublishSealed(pairings.Get(LinkTestTopics.Of("top-max"))!, $"{{\"plv\":5,\"id\":\"big\",\"result\":{{\"signedTx\":\"{signedTx}\"}}}}");

		var chunks = socket.SentFrames().Skip(2).Select(frame => (JObject)frame["payload"]!).ToArray();
		chunks.Length.ShouldBe(67);
		chunks.ShouldAllBe(chunk => (int)chunk["total"]! == 67);
		var assembled = string.Concat(chunks.OrderBy(chunk => (int)chunk["seq"]!).Select(chunk => (string)chunk["chunk"]!));
		var response = OpenSealedPayload(channel, assembled);
		((string)response["result"]!["signedTx"]!).Length.ShouldBe(44_739_244);
	}

	[Fact]
	public void A_response_above_the_budget_is_answered_with_5001()
	{
		// Spec section 11: a payload above the budget gets a clear 5001. The wallet sends a
		// small error with the request id in place of a response the relay cannot carry, so
		// the dApp does not wait for its timeout.
		var (endpoint, _, pairings, relay, sockets) = Build();
		var key = new byte[32];
		var channel = new LinkChannel(key);
		endpoint.TryHandle(RelayPairingUri(key, LinkTestTopics.Of("top-over"), Relay, "dapp-over"), _ => { }).ShouldBeTrue();
		var socket = sockets.Sockets[0];
		socket.Open();

		var signedTx = new string('A', 46_000_000); // longer than the base64 of 32 MiB
		relay.PublishSealed(pairings.Get(LinkTestTopics.Of("top-over"))!, $"{{\"plv\":5,\"id\":\"over\",\"result\":{{\"signedTx\":\"{signedTx}\"}}}}");

		var frames = socket.SentFrames().Skip(2).ToArray();
		frames.Length.ShouldBe(1);
		var error = OpenSealedPayload(channel, frames[0]["payload"]!);
		((string)error["id"]!).ShouldBe("over");
		((int)error["error"]!["code"]!).ShouldBe(5001);
		((int)error["error"]!["data"]!["maxPayloadBytes"]!).ShouldBe(32 * 1024 * 1024);
	}

	[Fact]
	public void Reconnects_resubscribes_and_flushes_frames_queued_while_offline()
	{
		var (endpoint, _, pairings, relay, sockets) = Build(new[] { 50 });
		var key = new byte[32];
		endpoint.TryHandle(RelayPairingUri(key, LinkTestTopics.Of("top-rc"), Relay, "rc-dapp"), _ => { }).ShouldBeTrue();
		var first = sockets.Sockets[0];
		first.Open();

		first.Drop();
		// A response produced while offline must queue, not vanish.
		relay.PublishSealed(pairings.Get(LinkTestTopics.Of("top-rc"))!, "{\"plv\":5,\"id\":\"x\",\"result\":{}}");

		// The 50 ms ladder should have redialed well within the wait.
		var deadline = DateTime.UtcNow.AddSeconds(2);
		while (sockets.Sockets.Count < 2 && DateTime.UtcNow < deadline)
		{
			Thread.Sleep(25);
		}
		sockets.Sockets.Count.ShouldBe(2);

		var second = sockets.Sockets[1];
		second.Open();
		var frames = second.SentFrames();
		((string)frames[0]["op"]!).ShouldBe("subscribe");
		((string)frames[0]["topic"]!).ShouldBe(LinkTestTopics.Of("top-rc") + ".dapp");
		// The queued publish flushed after the resubscribe (plus the original
		// sessionEstablished push, which also waited out the offline window).
		frames.Skip(1).ShouldAllBe(frame => (string?)frame["op"] == "publish");
		frames.Length.ShouldBeGreaterThanOrEqualTo(2);
	}

	[Fact]
	public void Ecdh_pairing_returns_the_wallet_key_and_a_usable_session()
	{
		var (endpoint, ops, _, _, sockets) = Build();

		// dApp side of the fallback: an ephemeral X25519 pair; ONLY the public key rides
		// the (hijackable) custom-scheme URI, together with the relay and the dApp meta.
		var (dappPublic, dappSecret) = PhantasmaPhoenix.Cryptography.NaCl.GenerateKeyPair();
		var pk = Convert.ToBase64String(dappPublic).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		var metaJson = "{\"name\":\"ecdh-dapp\",\"url\":\"https://dapp.example\"}";
		var meta = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(metaJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		var uri = $"phantasma://v5/pair#v=5&t={LinkTestTopics.Of("top-e")}&relay={Uri.EscapeDataString(Relay)}&pk={pk}&meta={meta}";

		endpoint.TryHandle(uri, _ => { }).ShouldBeTrue();
		ops.ConfirmPairingCalls.ShouldBe(1);
		var socket = sockets.Sockets[0];
		socket.Open();

		// The handshake payload carries the wallet's public key IN THE CLEAR next to the
		// sealed connect result; the dApp derives the same key via box.before.
		var frames = socket.SentFrames();
		((string)frames[0]["op"]!).ShouldBe("subscribe");
		var payload = (JObject)frames[1]["payload"]!;
		var wpk = (string)payload["wpk"]!;
		wpk.ShouldNotBeNullOrEmpty();
		var walletPublic = Convert.FromBase64String(wpk.Replace('-', '+').Replace('_', '/').PadRight((wpk.Length + 3) / 4 * 4, '='));
		var derived = PhantasmaPhoenix.Cryptography.NaCl.DeriveSessionKey(walletPublic, dappSecret);

		var channel = new LinkChannel(derived);
		var sealedFrame = new JObject { ["nonce"] = payload["nonce"], ["ct"] = payload["ct"] }.ToString();
		channel.TryOpenEnvelope(sealedFrame, out var envelopeJson).ShouldBeTrue();
		var pushed = JObject.Parse(envelopeJson);
		((string)pushed["event"]!).ShouldBe(WalletLinkV5.SessionEstablishedEvent);
		var sessionId = (string)pushed["data"]!["session"]!["id"]!;

		// The derived key now carries ordinary sealed traffic both ways.
		var request = $"{{\"plv\":5,\"id\":\"e1\",\"session\":\"{sessionId}\",\"method\":\"pha_getChains\"}}";
		socket.Deliver(new JObject
		{
			["op"] = "deliver",
			["topic"] = LinkTestTopics.Of("top-e") + ".dapp",
			["payload"] = channel.SealEnvelope(request),
		}.ToString());
		var response = OpenSealedPayload(channel, socket.SentFrames()[2]["payload"]!);
		((string)response["id"]!).ShouldBe("e1");
		((string)response["result"]!["nexus"]!).ShouldBe("localnet");
	}

	[Fact]
	public void Wake_deeplinks_reconnect_stored_relay_pairings()
	{
		var (endpoint, _, pairings, _, sockets) = Build();
		// A pairing persisted from an earlier run; no live connection yet.
		pairings.Save(new LinkPairingRecord
		{
			Topic = "top-w",
			Key = new byte[32],
			RelayUrl = Relay,
			DappName = "w-dapp",
			CreatedUtc = DateTime.UtcNow,
			LastSeenUtc = DateTime.UtcNow,
		});

		endpoint.TryHandle("phantasma://v5/wake#s=whatever", _ => { }).ShouldBeTrue();
		sockets.Sockets.Count.ShouldBe(1);
		sockets.Sockets[0].Open();
		((string)sockets.Sockets[0].SentFrames()[0]["op"]!).ShouldBe("subscribe");
	}

	[Fact]
	public void Over_cap_relay_sessions_evict_lru_and_unsubscribe()
	{
		// Cap of 2: a third, more-recent relay session must evict the least-recently-used one
		// (spec §7) - dropped from the store and unsubscribed on the live socket - so the relay
		// never refuses a fresh subscribe with topic_limit.
		var (_, _, pairings, relay, sockets) = Build(cap: 2);
		var t0 = DateTime.UtcNow;

		pairings.Save(RelayRecord("top-1", t0.AddSeconds(1)));
		relay.EnsureConnected();
		var socket = sockets.Sockets[0];
		socket.Open();
		pairings.Save(RelayRecord("top-2", t0.AddSeconds(2)));
		relay.EnsureConnected();
		pairings.Get("top-1").ShouldNotBeNull();
		pairings.Get("top-2").ShouldNotBeNull();

		// The newest pairing pushes the oldest out.
		pairings.Save(RelayRecord("top-3", t0.AddSeconds(3)));
		relay.EnsureConnected();

		pairings.Get("top-1").ShouldBeNull();        // evicted from the store
		pairings.Get("top-2").ShouldNotBeNull();
		pairings.Get("top-3").ShouldNotBeNull();
		var frames = socket.SentFrames();
		frames.ShouldContain(f => (string?)f["op"] == "unsubscribe" && (string?)f["topic"] == "top-1.dapp");
		frames.ShouldContain(f => (string?)f["op"] == "subscribe" && (string?)f["topic"] == "top-3.dapp");
	}

	[Fact]
	public void Idle_expired_relay_sessions_are_pruned()
	{
		// A pairing without a session, idle past the inactivity limit of 7 days, is dropped on
		// reconcile (spec §7), so dead pairings cannot pile up and starve fresh ones.
		var (_, _, pairings, relay, sockets) = Build();
		pairings.Save(RelayRecord("top-stale", DateTime.UtcNow.AddDays(-8))); // expired
		pairings.Save(RelayRecord("top-fresh", DateTime.UtcNow));             // live

		relay.EnsureConnected();

		pairings.Get("top-stale").ShouldBeNull();    // pruned
		pairings.Get("top-fresh").ShouldNotBeNull();
		sockets.Sockets[0].Open();
		var frames = sockets.Sockets[0].SentFrames();
		frames.ShouldContain(f => (string?)f["op"] == "subscribe" && (string?)f["topic"] == "top-fresh.dapp");
		frames.ShouldNotContain(f => ((string?)f["topic"] ?? "").StartsWith("top-stale"));
	}

	[Fact]
	public void Ending_the_session_of_a_deeplink_only_pairing_ends_it_without_the_relay()
	{
		// Regression (logout crash): a deeplink-only pairing has no RelayUrl and never subscribed a
		// relay topic. The end of its session must forget it WITHOUT touching the relay: a lookup
		// with its null relay URL used to throw and abort the wallet's logout.
		var ops = new FakeV5Ops();
		var dispatcher = new WalletLinkV5(ops);
		var pairings = new InMemoryLinkPairingStore();
		var sockets = new FakeRelaySocketFactory();
		using var relay = new LinkRelayClient(dispatcher, pairings, sockets, action => action());
		var endpoint = new LinkDeeplinkEndpoint(dispatcher, ops, pairings, relay);
		pairings.Save(new LinkPairingRecord
		{
			Topic = "top-dl",
			Key = new byte[32],
			RelayUrl = null,
			DappName = "dl-dapp",
			SessionId = "sess-dl",
			CreatedUtc = DateTime.UtcNow,
			LastSeenUtc = DateTime.UtcNow,
		});

		Should.NotThrow(() => dispatcher.EndSession("sess-dl", LinkSessionEndReason.Revoked));
		pairings.Get("top-dl").ShouldBeNull();   // the pairing is forgotten
		sockets.Sockets.ShouldBeEmpty();         // no relay connection was ever opened
		endpoint.ShouldNotBeNull();
	}

	// Spec §7, §18.3: when a session ends, its dApp gets the notice that fits over the pairing, and
	// the pairing ends with it: the wallet leaves the topic and forgets the key.
	[Theory]
	[InlineData(LinkSessionEndReason.Revoked, "pha_sessionDeleted")]
	[InlineData(LinkSessionEndReason.Evicted, "pha_sessionDeleted")]
	[InlineData(LinkSessionEndReason.Expired, "pha_sessionExpired")]
	public void Ending_a_session_sends_its_notice_and_ends_its_relay_pairing(LinkSessionEndReason reason, string notice)
	{
		var paired = PairOverRelay("end-" + reason);
		var before = paired.Socket.Sent.Count;

		paired.Dispatcher.EndSession(paired.SessionId, reason).ShouldBeTrue();

		var frames = paired.Socket.SentFrames().Skip(before).ToArray();
		var events = paired.Published(frames);
		events.Length.ShouldBe(1);
		((string)events[0]["event"]!).ShouldBe(notice);
		((string)events[0]["session"]!).ShouldBe(paired.SessionId);
		((string)events[0]["data"]!["session"]!).ShouldBe(paired.SessionId);
		frames.ShouldContain(f => (string?)f["op"] == "unsubscribe" && (string?)f["topic"] == paired.Topic + ".dapp");
		paired.Pairings.Get(paired.Topic).ShouldBeNull();
		paired.Sessions.Get(paired.SessionId).ShouldBeNull();
	}

	[Fact]
	public void A_disconnect_over_the_relay_is_answered_and_ends_the_pairing()
	{
		var paired = PairOverRelay("disconnect");

		var frames = paired.Request($"{{\"plv\":5,\"id\":\"d1\",\"session\":\"{paired.SessionId}\",\"method\":\"pha_disconnect\",\"params\":{{}}}}");

		// The notice first, then the answer: the transport still holds the key for it.
		var published = paired.Published(frames);
		((string)published[0]["event"]!).ShouldBe("pha_sessionDeleted");
		((string)published[1]["id"]!).ShouldBe("d1");
		((bool)published[1]["result"]!["ok"]!).ShouldBeTrue();
		frames.ShouldContain(f => (string?)f["op"] == "unsubscribe");
		paired.Pairings.Get(paired.Topic).ShouldBeNull();
	}

	[Fact]
	public void A_request_in_an_expired_session_gets_5100_and_the_pairing_ends()
	{
		// Spec §7: a wallet that finds the session ended when a request arrives, while it still
		// holds the pairing, answers 5100 over it.
		var paired = PairOverRelay("expired");
		paired.Sessions.Get(paired.SessionId)!.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1);

		var frames = paired.Request($"{{\"plv\":5,\"id\":\"x1\",\"session\":\"{paired.SessionId}\",\"method\":\"pha_getChains\"}}");

		var published = paired.Published(frames);
		((string)published[0]["event"]!).ShouldBe("pha_sessionExpired");
		((string)published[1]["id"]!).ShouldBe("x1");
		((int)published[1]["error"]!["code"]!).ShouldBe(5100);
		paired.Pairings.Get(paired.Topic).ShouldBeNull();
	}

	[Fact]
	public void A_connect_without_a_session_resumes_the_session_of_the_pairing()
	{
		// Spec §9.5: a late copy of the first pha_connect of a pairing changes nothing.
		var paired = PairOverRelay("late-connect");

		var frames = paired.Request("{\"plv\":5,\"id\":\"c1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"late-connect-dapp\",\"url\":\"https://dapp.example\"}}}");

		var answer = paired.Published(frames).Single();
		((string)answer["result"]!["session"]!["id"]!).ShouldBe(paired.SessionId);
		paired.Ops.ConnectCalls.ShouldBe(0);
	}

	[Theory]
	[InlineData("unknown", 4100)]
	[InlineData("expired", 5100)]
	public void A_resume_that_fails_over_the_relay_gets_an_error_and_never_a_second_session(string state, int code)
	{
		// Spec §9.5: over deeplink and relay a pairing carries one session and ends with it.
		var paired = PairOverRelay("resume-" + state);
		var session = paired.SessionId;
		if (state == "unknown")
		{
			session = "no-such-session";
		}
		else
		{
			paired.Sessions.Get(session)!.LastSeenUtc = DateTime.UtcNow.AddDays(-8);
		}

		var frames = paired.Request($"{{\"plv\":5,\"id\":\"c2\",\"session\":\"{session}\",\"method\":\"pha_connect\",\"params\":{{\"dapp\":{{\"name\":\"resume-{state}-dapp\",\"url\":\"https://dapp.example\"}}}}}}");

		var answer = paired.Published(frames).Single(e => (string?)e["id"] == "c2");
		((int)answer["error"]!["code"]!).ShouldBe(code);
		paired.Ops.ConnectCalls.ShouldBe(0);
	}

	[Fact]
	public void A_pairing_whose_session_the_wallet_no_longer_keeps_ends_on_reconnect()
	{
		// Spec §18.3: a pairing lives as long as its session. A session that went without its
		// pairing (a store that dropped it) ends the pairing with a notice at the next reconcile.
		var paired = PairOverRelay("orphan");
		paired.Sessions.Remove(paired.SessionId);
		var before = paired.Socket.Sent.Count;

		paired.Relay.EnsureConnected();

		var frames = paired.Socket.SentFrames().Skip(before).ToArray();
		((string)paired.Published(frames).Single()["event"]!).ShouldBe("pha_sessionDeleted");
		paired.Pairings.Get(paired.Topic).ShouldBeNull();
	}
}

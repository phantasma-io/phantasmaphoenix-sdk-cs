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

	private static (LinkDeeplinkEndpoint endpoint, FakeV5Ops ops, ILinkPairingStore pairings, LinkRelayClient relay, FakeRelaySocketFactory sockets) Build(int[]? ladder = null)
	{
		var ops = new FakeV5Ops();
		var dispatcher = new WalletLinkV5(ops);
		var pairings = new InMemoryLinkPairingStore();
		var sockets = new FakeRelaySocketFactory();
		// Inline marshal: tests run single-threaded, the Unity host passes PostToUi instead.
		var relay = new LinkRelayClient(dispatcher, pairings, sockets, action => action(), ladder);
		var endpoint = new LinkDeeplinkEndpoint(dispatcher, ops, pairings, relay);
		return (endpoint, ops, pairings, relay, sockets);
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

	[Fact]
	public void Hosts_normalize_to_wss_and_full_urls_pass_through()
	{
		// Spec section 17: `relay` is a host by default; explicit URLs serve local testing.
		LinkRelayClient.NormalizeRelayUrl("link.phantasma.info").ShouldBe("wss://link.phantasma.info/relay");
		LinkRelayClient.NormalizeRelayUrl("ws://localhost:7299/relay").ShouldBe("ws://localhost:7299/relay");
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
		endpoint.TryHandle(RelayPairingUri(key, "top-r", "ws://localhost:7299/relay", "qr-dapp"), _ => { }).ShouldBeTrue();
		sockets.Sockets.Count.ShouldBe(1);
		sockets.Sockets[0].Url.ShouldBe("ws://localhost:7299/relay");

		var socket = sockets.Sockets[0];
		socket.Open();
		var frames = socket.SentFrames();
		// First the topic subscription, then the one-tap sessionEstablished publish.
		((string)frames[0]["op"]!).ShouldBe("subscribe");
		((string)frames[0]["topic"]!).ShouldBe("top-r");
		((string)frames[1]["op"]!).ShouldBe("publish");
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
			["topic"] = "top-r",
			["payload"] = channel.SealEnvelope(request),
		}.ToString());

		var response = OpenSealedPayload(channel, socket.SentFrames()[2]["payload"]!);
		((string)response["id"]!).ShouldBe("r1");
		((string)response["result"]!["nexus"]!).ShouldBe("localnet");
	}

	[Fact]
	public void Chunked_requests_reassemble_and_large_responses_chunk()
	{
		var (endpoint, ops, _, _, sockets) = Build();
		// Make the wallet's answer far exceed the 900k chunk budget so the response MUST
		// leave as a chunked publish series.
		ops.InvokeResults = new[] { new string('A', 2_000_000) };
		var key = new byte[32];
		key[9] = 9;
		var channel = new LinkChannel(key);

		endpoint.TryHandle(RelayPairingUri(key, "top-big", "ws://localhost:7299/relay", "big-dapp"), _ => { }).ShouldBeTrue();
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
			["topic"] = "top-big",
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
		((string)response["result"]!["results"]![0]!).ShouldBe(ops.InvokeResults[0]);
	}

	[Fact]
	public void Reconnects_resubscribes_and_flushes_frames_queued_while_offline()
	{
		var (endpoint, _, pairings, relay, sockets) = Build(new[] { 50 });
		var key = new byte[32];
		endpoint.TryHandle(RelayPairingUri(key, "top-rc", "ws://localhost:7299/relay", "rc-dapp"), _ => { }).ShouldBeTrue();
		var first = sockets.Sockets[0];
		first.Open();

		first.Drop();
		// A response produced while offline must queue, not vanish.
		relay.PublishSealed(pairings.Get("top-rc")!, "{\"plv\":5,\"id\":\"x\",\"result\":{}}");

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
		((string)frames[0]["topic"]!).ShouldBe("top-rc");
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
		var uri = $"phantasma://v5/pair#v=5&t=top-e&relay={Uri.EscapeDataString("ws://localhost:7299/relay")}&pk={pk}&meta={meta}";

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
			["topic"] = "top-e",
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
			RelayUrl = "ws://localhost:7299/relay",
			DappName = "w-dapp",
			CreatedUtc = DateTime.UtcNow,
			LastSeenUtc = DateTime.UtcNow,
		});

		endpoint.TryHandle("phantasma://v5/wake#s=whatever", _ => { }).ShouldBeTrue();
		sockets.Sockets.Count.ShouldBe(1);
		sockets.Sockets[0].Open();
		((string)sockets.Sockets[0].SentFrames()[0]["op"]!).ShouldBe("subscribe");
	}
}

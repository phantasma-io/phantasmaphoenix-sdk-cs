using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Tests;

/// <summary>
/// The wallet-side deeplink endpoint: pairing consent, encrypted request dispatch, response URL
/// building - the full §19 flow with the OS hop replaced by direct calls. URL formats are pinned
/// to the TS SDK byte-for-byte via link-pairing-vectors.json.
/// </summary>
public class LinkDeeplinkEndpointTests
{
	private static readonly JObject Vectors = JObject.Parse(
		File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "link-pairing-vectors.json")));

	private static (LinkDeeplinkEndpoint endpoint, FakeV5Ops ops, ILinkPairingStore pairings, WalletLinkV5 dispatcher) Build()
	{
		var ops = new FakeV5Ops();
		var dispatcher = new WalletLinkV5(ops);
		var pairings = new InMemoryLinkPairingStore();
		return (new LinkDeeplinkEndpoint(dispatcher, ops, pairings), ops, pairings, dispatcher);
	}

	private static string SymPairingUri(byte[] key, string topic = "top-1", string callback = "https://dapp.example/app")
	{
		// Built the same way the TS SDK does (fragment query, base64url no-pad).
		var sk = Convert.ToBase64String(key).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		return $"https://link.phantasma.info/v5/pair#v=5&t={topic}&cb={Uri.EscapeDataString(callback)}&sk={sk}";
	}

	[Fact]
	public void Builds_request_and_response_urls_identical_to_the_TS_SDK()
	{
		var deeplink = (JObject)Vectors["deeplink"]!;
		var frame = (string)deeplink["frame"]!;

		var request = (JObject)deeplink["request"]!;
		LinkDeeplinkEndpoint.TryParseRequest((string)request["url"]!, out var topic, out var parsedFrame).ShouldBeTrue();
		topic.ShouldBe((string)request["topic"]!);
		parsedFrame.ShouldBe(frame);

		var response = (JObject)deeplink["response"]!;
		LinkDeeplinkEndpoint.BuildResponseUrl(
			(string)response["callback"]!,
			(string)response["topic"]!,
			frame).ShouldBe((string)response["url"]!);
	}

	[Fact]
	public void Pairing_consent_stores_the_channel_and_rejection_does_not()
	{
		var (endpoint, ops, pairings, _) = Build();
		var key = new byte[32];
		key[1] = 7;

		endpoint.TryHandle(SymPairingUri(key), _ => { }).ShouldBeTrue();
		ops.ConfirmPairingCalls.ShouldBe(1);
		ops.LastPairing!.DappName.ShouldBeNull();
		var record = pairings.Get("top-1");
		record.ShouldNotBeNull();
		record!.Key.ShouldBe(key);
		record.CallbackUrl.ShouldBe("https://dapp.example/app");

		var (endpoint2, ops2, pairings2, _) = Build();
		ops2.RejectPairing = true;
		endpoint2.TryHandle(SymPairingUri(key), _ => { }).ShouldBeTrue();
		pairings2.Get("top-1").ShouldBeNull();
	}

	[Fact]
	public void Pairing_without_callback_or_with_ecdh_mode_is_dropped_for_now()
	{
		var (endpoint, ops, pairings, _) = Build();

		// No cb: nowhere to deliver responses.
		endpoint.TryHandle("https://link.phantasma.info/v5/pair#v=5&t=x&sk=" + new string('A', 43), _ => { }).ShouldBeTrue();
		pairings.Get("x").ShouldBeNull();

		// ecdh: ships with the relay phase (needs a wallet-pubkey response hop).
		endpoint.TryHandle("phantasma://v5/pair#v=5&t=y&cb=https%3A%2F%2Fd.app&pk=" + new string('A', 43), _ => { }).ShouldBeTrue();
		pairings.Get("y").ShouldBeNull();
		ops.ConfirmPairingCalls.ShouldBe(0);
	}

	[Fact]
	public void Encrypted_request_round_trips_through_the_dispatcher_to_a_response_url()
	{
		var (endpoint, ops, pairings, _) = Build();
		var key = new byte[32];
		key[3] = 9;
		endpoint.TryHandle(SymPairingUri(key, "topic-7", "https://dapp.example/play"), _ => { }).ShouldBeTrue();
		pairings.Get("topic-7").ShouldNotBeNull();

		// dApp side: seal a connect envelope with the pairing key and wrap it as a request URL.
		var channel = new LinkChannel(key);
		var connectEnvelope = "{\"plv\":5,\"id\":\"c1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"dl-dapp\"}}}";
		var requestFrame = channel.SealEnvelope(connectEnvelope);
		var fb64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(requestFrame)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		var requestUrl = $"phantasma://v5/req#t=topic-7&f={fb64}";

		string? openedUrl = null;
		endpoint.TryHandle(requestUrl, url => openedUrl = url).ShouldBeTrue();

		// Wallet answered by opening the callback with a sealed response frame.
		openedUrl.ShouldNotBeNull();
		openedUrl!.StartsWith("https://dapp.example/play#plv=5&t=topic-7&f=").ShouldBeTrue();

		var f = openedUrl.Substring(openedUrl.IndexOf("&f=") + 3);
		var pad = f.Replace('-', '+').Replace('_', '/');
		pad += new string('=', (4 - pad.Length % 4) % 4);
		var responseFrame = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(pad));

		channel.TryOpenEnvelope(responseFrame, out var responseJson).ShouldBeTrue();
		var response = JObject.Parse(responseJson);
		((string)response["id"]!).ShouldBe("c1");
		((string)response["result"]!["account"]!["address"]!).ShouldBe("P2KTest");
		((string)response["result"]!["session"]!["id"]!).ShouldNotBeNullOrEmpty();
		ops.LastConnectDapp.ShouldBe("dl-dapp");
	}

	[Fact]
	public void Unknown_topics_forged_frames_and_foreign_urls_are_dropped()
	{
		var (endpoint, _, _, _) = Build();
		var opened = 0;

		// Unknown topic: no pairing, no key, no answer.
		endpoint.TryHandle("phantasma://v5/req#t=nope&f=AAAA", _ => opened++).ShouldBeTrue();

		// Paired, but the frame does not authenticate.
		var key = new byte[32];
		endpoint.TryHandle(SymPairingUri(key, "t9"), _ => opened++).ShouldBeTrue();
		var garbage = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"nonce\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"ct\":\"AAAA\"}")).TrimEnd('=');
		endpoint.TryHandle($"phantasma://v5/req#t=t9&f={garbage}", _ => opened++).ShouldBeTrue();

		// Not a v5 URL at all.
		endpoint.TryHandle("https://example.com/whatever#x=1", _ => opened++).ShouldBeFalse();
		endpoint.TryHandle("", _ => opened++).ShouldBeFalse();

		opened.ShouldBe(0);
	}
}

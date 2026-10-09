using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Tests;

/// <summary>
/// The wallet-side deeplink endpoint: pairing consent, encrypted request dispatch, response URL
/// building - the full §17 flow with the OS hop replaced by direct calls. URL formats are pinned
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

	private static string SymPairingUri(byte[] key, string? topic = null, string callback = "https://dapp.example/app", string? dappName = null)
	{
		topic ??= LinkTestTopics.Of("top-1");
		// Built the same way the TS SDK does (fragment query, base64url no-pad).
		var sk = Convert.ToBase64String(key).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		var uri = $"https://link.phantasma.info/v5/pair#v=5&t={topic}&cb={Uri.EscapeDataString(callback)}&sk={sk}";
		if (dappName != null)
		{
			var metaJson = $"{{\"name\":\"{dappName}\",\"url\":\"https://dapp.example\"}}";
			var meta = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(metaJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
			uri += $"&meta={meta}";
		}
		return uri;
	}

	private static string OpenResponseFrame(LinkChannel channel, string responseUrl)
	{
		// Unwrap a wallet->dApp response URL back to the sealed envelope's JSON.
		var f = responseUrl.Substring(responseUrl.IndexOf("&f=") + 3);
		var pad = f.Replace('-', '+').Replace('_', '/');
		pad += new string('=', (4 - pad.Length % 4) % 4);
		var frame = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(pad));
		channel.TryOpenEnvelope(frame, out var envelopeJson).ShouldBeTrue();
		return envelopeJson;
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
		var record = pairings.Get(LinkTestTopics.Of("top-1"));
		record.ShouldNotBeNull();
		record!.Key.ShouldBe(key);
		record.CallbackUrl.ShouldBe("https://dapp.example/app");

		var (endpoint2, ops2, pairings2, _) = Build();
		ops2.RejectPairing = true;
		endpoint2.TryHandle(SymPairingUri(key), _ => { }).ShouldBeTrue();
		pairings2.Get(LinkTestTopics.Of("top-1")).ShouldBeNull();
	}

	[Fact]
	public void Pairing_without_any_response_path_is_dropped()
	{
		var (endpoint, ops, pairings, _) = Build();

		// sym without cb AND without relay: nowhere to deliver responses.
		endpoint.TryHandle($"https://link.phantasma.info/v5/pair#v=5&t={LinkTestTopics.Of("x")}&sk=" + new string('A', 43), _ => { }).ShouldBeTrue();
		pairings.Get(LinkTestTopics.Of("x")).ShouldBeNull();

		// ecdh REQUIRES the relay for the wallet-pubkey hop; a callback cannot carry it.
		endpoint.TryHandle($"phantasma://v5/pair#v=5&t={LinkTestTopics.Of("y")}&cb=https%3A%2F%2Fd.app&pk=" + new string('A', 43), _ => { }).ShouldBeTrue();
		pairings.Get(LinkTestTopics.Of("y")).ShouldBeNull();
		ops.ConfirmPairingCalls.ShouldBe(0);
	}

	[Fact]
	public void Encrypted_request_round_trips_through_the_dispatcher_to_a_response_url()
	{
		var (endpoint, ops, pairings, _) = Build();
		var key = new byte[32];
		key[3] = 9;
		endpoint.TryHandle(SymPairingUri(key, LinkTestTopics.Of("topic-7"), "https://dapp.example/play"), _ => { }).ShouldBeTrue();
		pairings.Get(LinkTestTopics.Of("topic-7")).ShouldNotBeNull();

		// dApp side: seal a connect envelope with the pairing key and wrap it as a request URL.
		var channel = new LinkChannel(key);
		var connectEnvelope = "{\"plv\":5,\"id\":\"c1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"dl-dapp\",\"url\":\"https://dl-dapp.example\"}}}";
		var requestFrame = channel.SealEnvelope(connectEnvelope);
		var fb64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(requestFrame)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		var requestUrl = $"phantasma://v5/req#t={LinkTestTopics.Of("topic-7")}&f={fb64}";

		string? openedUrl = null;
		endpoint.TryHandle(requestUrl, url => openedUrl = url).ShouldBeTrue();

		// Wallet answered by opening the callback with a sealed response frame.
		openedUrl.ShouldNotBeNull();
		openedUrl!.StartsWith($"https://dapp.example/play#plv=5&t={LinkTestTopics.Of("topic-7")}&f=").ShouldBeTrue();

		var f = openedUrl.Substring(openedUrl.IndexOf("&f=") + 3);
		var pad = f.Replace('-', '+').Replace('_', '/');
		pad += new string('=', (4 - pad.Length % 4) % 4);
		var responseFrame = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(pad));

		channel.TryOpenEnvelope(responseFrame, out var responseJson).ShouldBeTrue();
		var response = JObject.Parse(responseJson);
		((string)response["id"]!).ShouldBe("c1");
		((string)response["result"]!["account"]!["address"]!).ShouldBe("P2KTest");
		var sessionId = (string)response["result"]!["session"]!["id"]!;
		sessionId.ShouldNotBeNullOrEmpty();
		ops.LastConnectDapp.ShouldBe("dl-dapp");
		// The pairing keeps the session of its explicit connect, so that the end of the session
		// can end the pairing too.
		pairings.Get(LinkTestTopics.Of("topic-7"))!.SessionId.ShouldBe(sessionId);
		ops.LastCaller!.Transport.ShouldBe(LinkTransportKind.Deeplink);
	}

	// Spec §18.3: a pairing that ended while its connect waited for the user stays ended. Saving it
	// again with the new session would bring its key back.
	[Fact]
	public void A_pairing_that_ended_during_its_connect_prompt_is_not_saved_again()
	{
		var (endpoint, ops, pairings, _) = Build();
		ops.DeferConnect = true;
		var key = new byte[32];
		key[3] = 5;
		var topic = LinkTestTopics.Of("ended-during-prompt");
		endpoint.TryHandle(SymPairingUri(key, topic), _ => { }).ShouldBeTrue();
		var channel = new LinkChannel(key);
		var frame = channel.SealEnvelope("{\"plv\":5,\"id\":\"c1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"dl-dapp\",\"url\":\"https://dl-dapp.example\"}}}");
		var fb64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(frame)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		endpoint.TryHandle($"phantasma://v5/req#t={topic}&f={fb64}", _ => { }).ShouldBeTrue();
		ops.PendingConnects.Count.ShouldBe(1);

		pairings.Remove(topic);
		ops.PendingConnects[0](LinkConnectResult.Ok(new LinkAccount { Address = "P2KTest" }, "FakeWallet", "9.9.9", "localnet"));

		pairings.Get(topic).ShouldBeNull();
	}

	[Fact]
	public void Pairing_approval_with_dapp_meta_pushes_a_usable_session_in_one_gesture()
	{
		var (endpoint, ops, _, _) = Build();
		var key = new byte[32];
		key[5] = 3;
		var channel = new LinkChannel(key);

		// Approval must answer by opening the callback with a sealed sessionEstablished event
		// (spec §15 step 3) - the one-tap first connection.
		string? openedUrl = null;
		endpoint.TryHandle(SymPairingUri(key, LinkTestTopics.Of("top-ot"), "https://dapp.example/app", dappName: "one-tap-dapp"), url => openedUrl = url).ShouldBeTrue();
		openedUrl.ShouldNotBeNull();
		openedUrl!.StartsWith($"https://dapp.example/app#plv=5&t={LinkTestTopics.Of("top-ot")}&f=").ShouldBeTrue();

		var envelope = JObject.Parse(OpenResponseFrame(channel, openedUrl));
		((string)envelope["type"]!).ShouldBe("event");
		((string)envelope["event"]!).ShouldBe(WalletLinkV5.SessionEstablishedEvent);
		var sessionId = (string)envelope["data"]!["session"]!["id"]!;
		sessionId.ShouldNotBeNullOrEmpty();
		((string)envelope["session"]!).ShouldBe(sessionId);
		((string)envelope["data"]!["account"]!["address"]!).ShouldBe("P2KTest");
		((string)envelope["data"]!["wallet"]!["name"]!).ShouldBe("FakeWallet");
		// The pairing consent IS the consent: the explicit connect prompt must NOT have run.
		ops.ConnectCalls.ShouldBe(0);

		// The pushed session is a real one: an in-session request authorizes promptlessly.
		var request = $"{{\"plv\":5,\"id\":\"r1\",\"session\":\"{sessionId}\",\"method\":\"pha_getChains\"}}";
		var fb64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(channel.SealEnvelope(request))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		string? chainsUrl = null;
		endpoint.TryHandle($"phantasma://v5/req#t={LinkTestTopics.Of("top-ot")}&f={fb64}", url => chainsUrl = url).ShouldBeTrue();
		chainsUrl.ShouldNotBeNull();
		var chains = JObject.Parse(OpenResponseFrame(channel, chainsUrl!));
		((string)chains["id"]!).ShouldBe("r1");
		((string)chains["result"]!["nexus"]!).ShouldBe("localnet");
	}

	// Spec §15: opening the same pairing link again opens no second session. The wallet asks
	// nothing, keeps the pairing with its key, and pushes the live session again.
	[Fact]
	public void A_pairing_link_opened_again_pushes_the_live_session_again()
	{
		var (endpoint, ops, pairings, dispatcher) = Build();
		var key = new byte[32];
		key[5] = 4;
		var channel = new LinkChannel(key);
		var uri = SymPairingUri(key, LinkTestTopics.Of("top-again"), "https://dapp.example/app", dappName: "again-dapp");
		var opened = new List<string>();
		endpoint.TryHandle(uri, opened.Add).ShouldBeTrue();
		var sessionId = (string)JObject.Parse(OpenResponseFrame(channel, opened.Single()))["session"]!;

		endpoint.TryHandle(uri, opened.Add).ShouldBeTrue();

		ops.ConfirmPairingCalls.ShouldBe(1);
		opened.Count.ShouldBe(2);
		var again = JObject.Parse(OpenResponseFrame(channel, opened[1]));
		((string)again["event"]!).ShouldBe(WalletLinkV5.SessionEstablishedEvent);
		((string)again["session"]!).ShouldBe(sessionId);
		pairings.List().Single().SessionId.ShouldBe(sessionId);
		dispatcher.IsSessionLive(sessionId).ShouldBeTrue();
	}

	[Fact]
	public void A_pairing_gives_its_session_the_lifetime_the_user_picked()
	{
		var (endpoint, ops, _, _) = Build();
		ops.PairingLifetime = LinkSessionLifetime.OneDay;
		var key = new byte[32];
		key[6] = 2;
		string? openedUrl = null;
		var start = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		endpoint.TryHandle(SymPairingUri(key, LinkTestTopics.Of("top-life"), "https://dapp.example/app", dappName: "life-dapp"), url => openedUrl = url).ShouldBeTrue();

		var envelope = JObject.Parse(OpenResponseFrame(new LinkChannel(key), openedUrl!));
		((long)envelope["data"]!["session"]!["expiresAt"]!).ShouldBeInRange(start + 86_400_000L, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 86_400_000L);
	}

	[Fact]
	public void A_deeplink_pairing_ends_with_its_session()
	{
		// Spec §18.3: the wallet forgets the pairing and its key when the session ends, so a later
		// request on it gets no answer.
		var (endpoint, _, pairings, dispatcher) = Build();
		var key = new byte[32];
		key[8] = 6;
		var channel = new LinkChannel(key);
		var topic = LinkTestTopics.Of("top-end");
		string? openedUrl = null;
		endpoint.TryHandle(SymPairingUri(key, topic, "https://dapp.example/app", dappName: "end-dapp"), url => openedUrl = url).ShouldBeTrue();
		var sessionId = (string)JObject.Parse(OpenResponseFrame(channel, openedUrl!))["data"]!["session"]!["id"]!;
		pairings.Get(topic)!.SessionId.ShouldBe(sessionId);

		dispatcher.EndSession(sessionId, LinkSessionEndReason.Revoked).ShouldBeTrue();

		pairings.Get(topic).ShouldBeNull();
		var request = $"{{\"plv\":5,\"id\":\"r1\",\"session\":\"{sessionId}\",\"method\":\"pha_getChains\"}}";
		var fb64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(channel.SealEnvelope(request))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		string? answer = null;
		endpoint.TryHandle($"phantasma://v5/req#t={topic}&f={fb64}", url => answer = url).ShouldBeTrue();
		answer.ShouldBeNull();
	}

	[Fact]
	public void Pairing_push_is_skipped_without_meta_name_or_without_a_ready_account()
	{
		// No dApp name in the pairing meta: the consent dialog could only show the bare topic,
		// so no account data may ride on it - the channel is stored, nothing is pushed.
		var (endpoint, _, pairings, _) = Build();
		var key = new byte[32];
		var opened = 0;
		endpoint.TryHandle(SymPairingUri(key, LinkTestTopics.Of("t-noname")), _ => opened++).ShouldBeTrue();
		opened.ShouldBe(0);
		pairings.Get(LinkTestTopics.Of("t-noname")).ShouldNotBeNull();

		// Wallet not Ready: same graceful degradation to the classic two-step connect.
		var (endpoint2, ops2, pairings2, _) = Build();
		ops2.StatusValue = WalletStatus.Closed;
		endpoint2.TryHandle(SymPairingUri(key, LinkTestTopics.Of("t-closed"), dappName: "d"), _ => opened++).ShouldBeTrue();
		opened.ShouldBe(0);
		pairings2.Get(LinkTestTopics.Of("t-closed")).ShouldNotBeNull();

		// Account unavailable: pairing stored, no push.
		var (endpoint3, ops3, pairings3, _) = Build();
		ops3.FailAccount = LinkFailure.NotLoggedIn;
		endpoint3.TryHandle(SymPairingUri(key, LinkTestTopics.Of("t-noacc"), dappName: "d"), _ => opened++).ShouldBeTrue();
		opened.ShouldBe(0);
		pairings3.Get(LinkTestTopics.Of("t-noacc")).ShouldNotBeNull();
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
		endpoint.TryHandle(SymPairingUri(key, LinkTestTopics.Of("t9")), _ => opened++).ShouldBeTrue();
		var garbage = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"nonce\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"ct\":\"AAAA\"}")).TrimEnd('=');
		endpoint.TryHandle($"phantasma://v5/req#t={LinkTestTopics.Of("t9")}&f={garbage}", _ => opened++).ShouldBeTrue();

		// Not a v5 URL at all.
		endpoint.TryHandle("https://example.com/whatever#x=1", _ => opened++).ShouldBeFalse();
		endpoint.TryHandle("", _ => opened++).ShouldBeFalse();

		opened.ShouldBe(0);
	}
}

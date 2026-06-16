using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.Cryptography;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Tests;

public class WalletLinkV5Tests
{
	private static JObject Send(WalletLinkV5 link, string json)
	{
		JObject? response = null;
		link.HandleMessage(json, s => response = JObject.Parse(s));
		response.ShouldNotBeNull();
		return response!;
	}

	private static string Connect(WalletLinkV5 link, out JObject connectResponse)
	{
		connectResponse = Send(link,
			"{\"plv\":5,\"id\":\"c1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"testdapp\",\"url\":\"https://t.example\"}}}");
		return (string)connectResponse["result"]!["session"]!["id"]!;
	}

	[Fact]
	public void Connect_establishes_a_session_and_returns_wallet_account_and_capabilities()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);

		var session = Connect(link, out var response);

		((int)response["plv"]!).ShouldBe(5);
		((string)response["id"]!).ShouldBe("c1");
		session.ShouldNotBeNullOrEmpty();
		ops.LastConnectDapp.ShouldBe("testdapp");
		// The dispatcher OWNS and registers the session id (the op receives no token): the issued
		// id must authorize a follow-up request instead of being rejected as an unknown session.
		var authorized = Send(link, $"{{\"plv\":5,\"id\":\"c1b\",\"session\":\"{session}\",\"method\":\"pha_getChains\"}}");
		authorized["error"].ShouldBeNull();
		((string)response["result"]!["wallet"]!["name"]!).ShouldBe("FakeWallet");
		((string)response["result"]!["account"]!["address"]!).ShouldBe("P2KTest");
		((string)response["result"]!["capabilities"]!["chains"]![0]!).ShouldBe("phantasma:localnet");
	}

	[Fact]
	public void Requests_without_a_session_are_unauthorized()
	{
		var link = new WalletLinkV5(new FakeV5Ops());

		var response = Send(link, "{\"plv\":5,\"id\":\"r1\",\"method\":\"pha_getChains\"}");

		((int)response["error"]!["code"]!).ShouldBe(4100);
	}

	[Fact]
	public void SendTransaction_carbon_routes_to_ops_and_returns_the_hash()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);

		var txBase64 = Convert.ToBase64String(new byte[] { 0xAA, 0xBB, 0xCC });
		var response = Send(link,
			$"{{\"plv\":5,\"id\":\"t1\",\"session\":\"{session}\",\"method\":\"pha_sendTransaction\",\"params\":{{\"format\":\"carbon\",\"tx\":\"{txBase64}\"}}}}");

		ops.SentFormat.ShouldBe(LinkTxFormat.Carbon);
		ops.SentTx.ShouldBe(new byte[] { 0xAA, 0xBB, 0xCC });
		((string)response["result"]!["hash"]!).ShouldNotBeNullOrEmpty();
	}

	[Theory]
	[InlineData(LinkFailure.UserRejected, 4001)]
	[InlineData(LinkFailure.InvalidTransaction, -32602)]
	[InlineData(LinkFailure.NotLoggedIn, 4900)]
	[InlineData(LinkFailure.Internal, -32603)]
	public void SendTransaction_structured_failures_map_to_distinct_codes(LinkFailure failure, int expectedCode)
	{
		// The clean contract reports WHY through LinkFailure; the dispatcher maps each to a code the
		// dApp can branch on (no free-text matching). User rejection is NOT a generic error.
		var ops = new FakeV5Ops { FailSend = failure, FailSendMessage = "detail" };
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);

		var response = Send(link,
			$"{{\"plv\":5,\"id\":\"f1\",\"session\":\"{session}\",\"method\":\"pha_sendTransaction\",\"params\":{{\"format\":\"carbon\",\"tx\":\"AA==\"}}}}");

		((int)response["error"]!["code"]!).ShouldBe(expectedCode);
		((string)response["error"]!["message"]!).ShouldBe("detail");
	}

	[Fact]
	public void SendTransaction_validates_tx_bytes_before_touching_the_wallet()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);

		var empty = Send(link,
			$"{{\"plv\":5,\"id\":\"e1\",\"session\":\"{session}\",\"method\":\"pha_sendTransaction\",\"params\":{{\"format\":\"carbon\",\"tx\":\"\"}}}}");
		((int)empty["error"]!["code"]!).ShouldBe(-32602);

		var garbage = Send(link,
			$"{{\"plv\":5,\"id\":\"e2\",\"session\":\"{session}\",\"method\":\"pha_sendTransaction\",\"params\":{{\"format\":\"carbon\",\"tx\":\"!!!\"}}}}");
		((int)garbage["error"]!["code"]!).ShouldBe(-32602);

		var badFormat = Send(link,
			$"{{\"plv\":5,\"id\":\"e3\",\"session\":\"{session}\",\"method\":\"pha_sendTransaction\",\"params\":{{\"format\":\"bogus\",\"tx\":\"AA==\"}}}}");
		((int)badFormat["error"]!["code"]!).ShouldBe(-32602);

		ops.SentFormat.ShouldBeNull(); // the wallet op was never called
	}

	[Fact]
	public void Connect_rejection_maps_to_user_rejected_and_leaves_no_session()
	{
		var ops = new FakeV5Ops { RejectConnect = true };
		var link = new WalletLinkV5(ops);

		var response = Send(link,
			"{\"plv\":5,\"id\":\"c1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"d\"}}}");
		((int)response["error"]!["code"]!).ShouldBe(4001);

		// No session leaked: a follow-up using a made-up session is still unauthorized.
		var after = Send(link, "{\"plv\":5,\"id\":\"c2\",\"session\":\"whatever\",\"method\":\"pha_getChains\"}");
		((int)after["error"]!["code"]!).ShouldBe(4100);
	}

	[Fact]
	public void Connect_failure_then_retry_succeeds()
	{
		var ops = new FakeV5Ops { FailConnect = LinkFailure.Internal, FailConnectMessage = "account unavailable" };
		var link = new WalletLinkV5(ops);

		var failed = Send(link, "{\"plv\":5,\"id\":\"c1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"d\"}}}");
		((int)failed["error"]!["code"]!).ShouldBe(-32603);

		ops.FailConnect = LinkFailure.None;
		var ok = Send(link, "{\"plv\":5,\"id\":\"c2\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"d\"}}}");
		((string)ok["result"]!["session"]!["id"]!).ShouldNotBeNullOrEmpty();
	}

	[Fact]
	public void Requests_while_wallet_is_closed_are_disconnected()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);

		ops.StatusValue = WalletStatus.Closed;
		var response = Send(link, $"{{\"plv\":5,\"id\":\"r1\",\"session\":\"{session}\",\"method\":\"pha_getChains\"}}");
		((int)response["error"]!["code"]!).ShouldBe(4900);
	}

	[Fact]
	public void Wrong_protocol_version_and_unknown_methods_are_rejected()
	{
		var link = new WalletLinkV5(new FakeV5Ops());
		var session = Connect(link, out _);

		var wrongPlv = Send(link, "{\"plv\":4,\"id\":\"x1\",\"method\":\"pha_getChains\"}");
		((int)wrongPlv["error"]!["code"]!).ShouldBe(-32600);

		var unknown = Send(link, $"{{\"plv\":5,\"id\":\"x2\",\"session\":\"{session}\",\"method\":\"pha_nope\"}}");
		((int)unknown["error"]!["code"]!).ShouldBe(-32601);
	}

	[Fact]
	public void Disconnect_is_idempotent_and_invalidates_the_session()
	{
		var link = new WalletLinkV5(new FakeV5Ops());
		var session = Connect(link, out _);

		var first = Send(link, $"{{\"plv\":5,\"id\":\"d1\",\"session\":\"{session}\",\"method\":\"pha_disconnect\"}}");
		((bool)first["result"]!["ok"]!).ShouldBeTrue();

		var after = Send(link, $"{{\"plv\":5,\"id\":\"d2\",\"session\":\"{session}\",\"method\":\"pha_getChains\"}}");
		((int)after["error"]!["code"]!).ShouldBe(4100);
	}

	[Fact]
	public void Capabilities_advertise_the_full_method_surface()
	{
		var link = new WalletLinkV5(new FakeV5Ops());
		Connect(link, out var response);

		// The handshake must advertise the complete contract surface (sign operations and
		// both tx formats) so capability-aware dApps know they can use them.
		var methods = ((JArray)response["result"]!["capabilities"]!["methods"]!).Select(t => (string)t!).ToArray();
		methods.ShouldContain("pha_signMessage");
		methods.ShouldContain("pha_signTransaction");
		var formats = ((JArray)response["result"]!["capabilities"]!["txFormats"]!).Select(t => (string)t!).ToArray();
		formats.ShouldBe(new[] { "script", "carbon" });
	}

	[Fact]
	public void SignMessage_routes_to_ops_and_returns_signature_and_random()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);

		var messageB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("hello"));
		var response = Send(link, $"{{\"plv\":5,\"id\":\"m1\",\"session\":\"{session}\",\"method\":\"pha_signMessage\",\"params\":{{\"message\":\"{messageB64}\",\"display\":\"hi\"}}}}");

		ops.SignedMessage.ShouldBe(System.Text.Encoding.UTF8.GetBytes("hello"));
		ops.SignedDisplay.ShouldBe("hi");
		Convert.FromBase64String((string)response["result"]!["signature"]!)[0].ShouldBe((byte)9);
		Convert.FromBase64String((string)response["result"]!["random"]!).Length.ShouldBe(LinkSignMessage.RandomLength);
	}

	[Fact]
	public void SignMessage_validates_params_and_maps_failures()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);

		// Missing and non-base64 messages never reach the wallet.
		var missing = Send(link, $"{{\"plv\":5,\"id\":\"m2\",\"session\":\"{session}\",\"method\":\"pha_signMessage\",\"params\":{{}}}}");
		((int)missing["error"]!["code"]!).ShouldBe(-32602);
		var garbage = Send(link, $"{{\"plv\":5,\"id\":\"m3\",\"session\":\"{session}\",\"method\":\"pha_signMessage\",\"params\":{{\"message\":\"%%%\"}}}}");
		((int)garbage["error"]!["code"]!).ShouldBe(-32602);
		ops.SignedMessage.ShouldBeNull();

		// A wallet-side rejection surfaces as the structured user-rejected code.
		ops.FailSignMessage = LinkFailure.UserRejected;
		var rejected = Send(link, $"{{\"plv\":5,\"id\":\"m4\",\"session\":\"{session}\",\"method\":\"pha_signMessage\",\"params\":{{\"message\":\"AA==\"}}}}");
		((int)rejected["error"]!["code"]!).ShouldBe(4001);
	}

	[Fact]
	public void SignTransaction_routes_and_returns_the_signed_tx()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);

		var response = Send(link, $"{{\"plv\":5,\"id\":\"st1\",\"session\":\"{session}\",\"method\":\"pha_signTransaction\",\"params\":{{\"format\":\"carbon\",\"tx\":\"AAEC\"}}}}");

		ops.SignTxFormat.ShouldBe(LinkTxFormat.Carbon);
		// The fake prefixes 0x5A; the dispatcher must return the bytes verbatim as base64.
		var signed = Convert.FromBase64String((string)response["result"]!["signedTx"]!);
		signed[0].ShouldBe((byte)0x5A);
		signed.Skip(1).ToArray().ShouldBe(Convert.FromBase64String("AAEC"));

		// Structured unsupported-kind failure maps to 5003.
		ops.FailSignTx = LinkFailure.UnsupportedSignatureKind;
		var unsupported = Send(link, $"{{\"plv\":5,\"id\":\"st2\",\"session\":\"{session}\",\"method\":\"pha_signTransaction\",\"params\":{{\"format\":\"carbon\",\"tx\":\"AAEC\",\"signatureKind\":\"ECDSA\"}}}}");
		((int)unsupported["error"]!["code"]!).ShouldBe(5003);
	}

	[Fact]
	public void Oversized_transactions_are_refused_with_a_structured_5001()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);

		// One char beyond the base64 ceiling of the chain's 32 MiB max-tx: the guard must
		// fire BEFORE base64 decoding or any wallet involvement, carrying the limit.
		var oversized = new string('A', (32 * 1024 * 1024 + 2) / 3 * 4 + 1);
		var response = Send(link, $"{{\"plv\":5,\"id\":\"big\",\"session\":\"{session}\",\"method\":\"pha_signTransaction\",\"params\":{{\"format\":\"carbon\",\"tx\":\"{oversized}\"}}}}");

		((int)response["error"]!["code"]!).ShouldBe(5001);
		((int)response["error"]!["data"]!["maxPayloadBytes"]!).ShouldBe(32 * 1024 * 1024);
		ops.SignTxFormat.ShouldBeNull(); // the wallet was never touched
	}

	[Fact]
	public void SignMessage_payload_layout_is_tag_random_message()
	{
		// The byte layout is the cross-language contract (mirrors the TS SDK constant and
		// buildSignMessagePayload); any drift here breaks signature verification.
		var random = new byte[LinkSignMessage.RandomLength];
		for (var i = 0; i < random.Length; i++) random[i] = (byte)i;
		var message = new byte[] { 0xCA, 0xFE };
		var payload = LinkSignMessage.BuildPayload(message, random);

		var tag = System.Text.Encoding.ASCII.GetBytes("PHANTASMA_LINK_V5_MSG\n");
		payload.Take(tag.Length).ToArray().ShouldBe(tag);
		payload.Skip(tag.Length).Take(random.Length).ToArray().ShouldBe(random);
		payload.Skip(tag.Length + random.Length).ToArray().ShouldBe(message);

		// CSPRNG sanity: correct length and two draws differ.
		LinkSignMessage.GenerateRandom().Length.ShouldBe(32);
		LinkSignMessage.GenerateRandom().ShouldNotBe(LinkSignMessage.GenerateRandom());
	}

	[Fact]
	public void InvokeScript_returns_decoded_results_and_maps_failures()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);

		var ok = Send(link,
			$"{{\"plv\":5,\"id\":\"i1\",\"session\":\"{session}\",\"method\":\"pha_invokeScript\",\"params\":{{\"chain\":\"main\",\"script\":\"AA==\"}}}}");
		((JArray)ok["result"]!["results"]!).Single().Value<string>().ShouldBe("AABB");

		ops.FailInvoke = LinkFailure.Internal;
		var fail = Send(link,
			$"{{\"plv\":5,\"id\":\"i2\",\"session\":\"{session}\",\"method\":\"pha_invokeScript\",\"params\":{{\"chain\":\"main\",\"script\":\"AA==\"}}}}");
		((int)fail["error"]!["code"]!).ShouldBe(-32603);
	}

	[Fact]
	public void Concurrent_request_while_a_prompt_is_pending_is_rejected_then_recovers()
	{
		var ops = new FakeV5Ops { DeferConnect = true };
		var link = new WalletLinkV5(ops);

		JObject? first = null;
		link.HandleMessage(
			"{\"plv\":5,\"id\":\"c1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"a\"}}}",
			s => first = JObject.Parse(s));
		first.ShouldBeNull(); // prompt still open

		var second = Send(link, "{\"plv\":5,\"id\":\"c2\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"b\"}}}");
		((int)second["error"]!["code"]!).ShouldBe(-32603);
		((string)second["error"]!["message"]!).ShouldContain("pending");

		// Approve the original prompt: it completes and unlocks the dispatcher.
		ops.DeferConnect = false;
		ops.PendingConnect!(LinkConnectResult.Ok(new LinkAccount { Address = "P2KTest", Name = "tester" }, "FakeWallet", "9.9.9", "localnet"));
		first.ShouldNotBeNull();
		((string)first!["result"]!["session"]!["id"]!).ShouldNotBeNullOrEmpty();

		var third = Send(link, "{\"plv\":5,\"id\":\"c3\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"c\"}}}");
		((string)third["result"]!["session"]!["id"]!).ShouldNotBeNullOrEmpty();
	}

	[Fact]
	public void Abandoned_pending_request_can_be_taken_over_after_the_timeout()
	{
		var ops = new FakeV5Ops { DeferConnect = true };
		var link = new WalletLinkV5(ops) { PendingTakeoverSeconds = 0 };

		JObject? stale = null;
		link.HandleMessage(
			"{\"plv\":5,\"id\":\"c1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"stale\"}}}",
			s => stale = JObject.Parse(s));
		stale.ShouldBeNull();

		// Takeover timeout is 0s: a new connect proceeds instead of being rejected.
		ops.DeferConnect = false;
		var fresh = Send(link, "{\"plv\":5,\"id\":\"c2\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"fresh\"}}}");
		((string)fresh["result"]!["session"]!["id"]!).ShouldNotBeNullOrEmpty();
	}

	[Fact]
	public void Malformed_envelopes_get_parse_and_invalid_request_errors()
	{
		var link = new WalletLinkV5(new FakeV5Ops());

		var notJson = Send(link, "not json at all");
		((int)notJson["error"]!["code"]!).ShouldBe(-32700);

		var noMethod = Send(link, "{\"plv\":5,\"id\":\"x1\"}");
		((int)noMethod["error"]!["code"]!).ShouldBe(-32600);
	}
	[Fact]
	public void Resume_with_known_session_skips_the_prompt_and_keeps_the_id()
	{
		// Spec §7: a dApp presenting its known session id reconnects WITHOUT a consent prompt.
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);
		ops.ConnectCalls.ShouldBe(1);

		var resumed = Send(link,
			$"{{\"plv\":5,\"id\":\"r1\",\"method\":\"pha_connect\",\"params\":{{\"dapp\":{{\"name\":\"testdapp\"}},\"session\":\"{session}\"}}}}");

		ops.ConnectCalls.ShouldBe(1); // NO second prompt
		((string)resumed["result"]!["session"]!["id"]!).ShouldBe(session); // same session id
		((string)resumed["result"]!["account"]!["address"]!).ShouldBe("P2KTest");
		((string)resumed["result"]!["wallet"]!["name"]!).ShouldBe("FakeWallet");
	}

	[Fact]
	public void Resume_with_unknown_id_falls_back_to_the_consent_prompt()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);

		var response = Send(link,
			"{\"plv\":5,\"id\":\"r1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"testdapp\"},\"session\":\"nope\"}}");

		ops.ConnectCalls.ShouldBe(1); // prompted
		((string)response["result"]!["session"]!["id"]!).ShouldNotBe("nope"); // fresh session
	}

	[Fact]
	public void Resume_with_a_different_dapp_name_falls_back_to_the_consent_prompt()
	{
		// A session is bound to the dApp identity the user approved; another dApp cannot ride it.
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);

		var response = Send(link,
			$"{{\"plv\":5,\"id\":\"r1\",\"method\":\"pha_connect\",\"params\":{{\"dapp\":{{\"name\":\"OTHER\"}},\"session\":\"{session}\"}}}}");

		ops.ConnectCalls.ShouldBe(2); // re-consent for the other dApp
		((string)response["result"]!["session"]!["id"]!).ShouldNotBe(session);
	}

	[Fact]
	public void Resume_after_account_switch_falls_back_to_the_consent_prompt()
	{
		// The session is bound to the account it authorized; a switched wallet re-prompts.
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);

		ops.AccountAddress = "P2KOther";
		var response = Send(link,
			$"{{\"plv\":5,\"id\":\"r1\",\"method\":\"pha_connect\",\"params\":{{\"dapp\":{{\"name\":\"testdapp\"}},\"session\":\"{session}\"}}}}");

		ops.ConnectCalls.ShouldBe(2);
		((string)response["result"]!["session"]!["id"]!).ShouldNotBe(session);
		((string)response["result"]!["account"]!["address"]!).ShouldBe("P2KOther");
	}

	[Fact]
	public void Sessions_survive_a_dispatcher_restart_when_the_store_is_durable()
	{
		// Headless equivalent of a wallet restart: a NEW dispatcher over the SAME durable store
		// must resume the session without a prompt (spec §7 persistence).
		var store = new InMemoryLinkSessionStore();
		var ops = new FakeV5Ops();

		var first = new WalletLinkV5(ops, store);
		var session = Connect(first, out _);
		ops.ConnectCalls.ShouldBe(1);

		var second = new WalletLinkV5(ops, store); // "restarted" wallet
		var resumed = Send(second,
			$"{{\"plv\":5,\"id\":\"r1\",\"method\":\"pha_connect\",\"params\":{{\"dapp\":{{\"name\":\"testdapp\"}},\"session\":\"{session}\"}}}}");

		ops.ConnectCalls.ShouldBe(1); // still no second prompt
		((string)resumed["result"]!["session"]!["id"]!).ShouldBe(session);

		// And the resumed session is live for authenticated calls on the new dispatcher.
		var chains = Send(second, $"{{\"plv\":5,\"id\":\"r2\",\"session\":\"{session}\",\"method\":\"pha_getChains\"}}");
		((string)chains["result"]!["nexus"]!).ShouldBe("localnet");
	}
}

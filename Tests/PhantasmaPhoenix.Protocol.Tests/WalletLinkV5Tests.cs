using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.Cryptography;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Tests;

public class WalletLinkV5Tests
{
	/// <summary>
	/// Deterministic in-memory implementation of the clean v5 wallet contract. Switches drive every
	/// dispatcher branch (rejection, deferred consent, structured failures) without any Unity host.
	/// </summary>
	private sealed class FakeV5Ops : IWalletLinkV5Ops
	{
		public WalletStatus StatusValue = WalletStatus.Ready;
		public string AccountAddress = "P2KTest";

		// Connect controls.
		public bool RejectConnect;
		public LinkFailure FailConnect = LinkFailure.None;
		public string? FailConnectMessage;
		public bool DeferConnect;
		public Action<LinkConnectResult>? PendingConnect;
		public string? LastConnectToken;
		public string? LastConnectDapp;
		/// <summary>How many times the consent flow (Connect) ran - resume must NOT increase it.</summary>
		public int ConnectCalls;

		// Other-op controls.
		public LinkFailure FailAccount = LinkFailure.None;
		public LinkFailure FailSend = LinkFailure.None;
		public string? FailSendMessage;
		public LinkFailure FailInvoke = LinkFailure.None;
		public LinkTxFormat? SentFormat;
		public byte[]? SentTx;

		public WalletStatus Status => StatusValue;

		private LinkAccount Account() => new LinkAccount
		{
			Address = AccountAddress,
			Name = "tester",
			Avatar = "",
			Balances = new[]
			{
				new LinkBalance { Symbol = "SOUL", Value = "1", Decimals = 8 },
			},
		};

		public void Connect(string dappName, string sessionToken, Action<LinkConnectResult> done)
		{
			ConnectCalls++;
			LastConnectDapp = dappName;
			LastConnectToken = sessionToken;
			if (DeferConnect) { PendingConnect = done; return; }
			if (RejectConnect) { done(LinkConnectResult.Fail(LinkFailure.UserRejected, "rejected")); return; }
			if (FailConnect != LinkFailure.None) { done(LinkConnectResult.Fail(FailConnect, FailConnectMessage)); return; }
			done(LinkConnectResult.Ok(Account(), "FakeWallet", "9.9.9", "localnet"));
		}

		public void GetAccount(Action<LinkAccountResult> done)
		{
			if (FailAccount != LinkFailure.None) { done(LinkAccountResult.Fail(FailAccount, "account unavailable")); return; }
			done(LinkAccountResult.Ok(Account()));
		}

		public void GetChains(Action<LinkChains> done) => done(new LinkChains { Nexus = "localnet" });

		public void GetWalletInfo(Action<LinkWalletInfo> done) =>
			done(new LinkWalletInfo { Name = "FakeWallet", Version = "9.9.9", Rpc = "http://localhost:7077/rpc" });

		public void SendTransaction(byte[] serializedTx, LinkTxFormat format, SignatureKind kind, ProofOfWork pow, Action<LinkSendResult> done)
		{
			SentFormat = format;
			SentTx = serializedTx;
			if (FailSend != LinkFailure.None) { done(LinkSendResult.Fail(FailSend, FailSendMessage)); return; }
			var hashBytes = new byte[32];
			hashBytes[0] = 1;
			done(LinkSendResult.Ok(Hash.FromBytes(hashBytes)));
		}

		public void InvokeScript(string chain, byte[] script, Action<LinkInvokeResult> done)
		{
			if (FailInvoke != LinkFailure.None) { done(LinkInvokeResult.Fail(FailInvoke, "invoke failed")); return; }
			done(LinkInvokeResult.Ok(new[] { "AABB" }));
		}
	}

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
		ops.LastConnectToken.ShouldBe(session); // the dispatcher issues the token and registers it
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
	public void Capabilities_advertise_only_what_the_wallet_implements()
	{
		var link = new WalletLinkV5(new FakeV5Ops());
		var session = Connect(link, out var response);

		var methods = ((JArray)response["result"]!["capabilities"]!["methods"]!).Select(t => (string)t!).ToArray();
		methods.ShouldNotContain("pha_signMessage");
		methods.ShouldNotContain("pha_signTransaction");
		var formats = ((JArray)response["result"]!["capabilities"]!["txFormats"]!).Select(t => (string)t!).ToArray();
		formats.ShouldBe(new[] { "carbon" });

		var sign = Send(link, $"{{\"plv\":5,\"id\":\"s1\",\"session\":\"{session}\",\"method\":\"pha_signMessage\",\"params\":{{\"message\":\"AA==\"}}}}");
		((int)sign["error"]!["code"]!).ShouldBe(5004);
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

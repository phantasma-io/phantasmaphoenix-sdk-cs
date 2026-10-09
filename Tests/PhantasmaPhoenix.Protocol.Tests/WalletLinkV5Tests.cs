using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.Cryptography;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Tests;

public class WalletLinkV5Tests
{
	private static JObject Send(WalletLinkV5 link, string json, LinkRequestContext? context = null)
	{
		JObject? response = null;
		link.HandleMessage(json, s => response = JObject.Parse(s), context);
		response.ShouldNotBeNull();
		return response!;
	}

	private static int _connects;

	/// <summary>Connects a new session. Each connect has its own request id: the wallet drops a copy
	/// of a connect it saw on the channel (spec §18.2).</summary>
	private static string Connect(WalletLinkV5 link, out JObject connectResponse, string? id = null)
	{
		id ??= "c" + System.Threading.Interlocked.Increment(ref _connects);
		connectResponse = Send(link,
			$"{{\"plv\":5,\"id\":\"{id}\",\"method\":\"pha_connect\",\"params\":{{\"dapp\":{{\"name\":\"testdapp\",\"url\":\"https://t.example\"}}}}}}");
		return (string)connectResponse["result"]!["session"]!["id"]!;
	}

	[Fact]
	public void Connect_establishes_a_session_and_returns_wallet_account_and_capabilities()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);

		var session = Connect(link, out var response, "c1");

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

		// Spec §9.4: a format the wallet does not support gets 5004, a missing one -32602.
		var badFormat = Send(link,
			$"{{\"plv\":5,\"id\":\"e3\",\"session\":\"{session}\",\"method\":\"pha_sendTransaction\",\"params\":{{\"format\":\"bogus\",\"tx\":\"AA==\"}}}}");
		((int)badFormat["error"]!["code"]!).ShouldBe(5004);
		var noFormat = Send(link,
			$"{{\"plv\":5,\"id\":\"e4\",\"session\":\"{session}\",\"method\":\"pha_sendTransaction\",\"params\":{{\"tx\":\"AA==\"}}}}");
		((int)noFormat["error"]!["code"]!).ShouldBe(-32602);

		ops.SentFormat.ShouldBeNull(); // the wallet op was never called
	}

	// Spec 1.2 has no `pow`: the wallet signs the transaction exactly as the dApp built it. A dApp
	// that still sends the field gets its transaction signed, because a receiver ignores a field it
	// does not know (spec §4).
	[Fact]
	public void A_pow_param_from_an_older_dApp_is_ignored()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);

		var response = Send(link,
			$"{{\"plv\":5,\"id\":\"w1\",\"session\":\"{session}\",\"method\":\"pha_signTransaction\",\"params\":{{\"format\":\"script\",\"tx\":\"AAEC\",\"pow\":{{}}}}}}");

		response["error"].ShouldBeNull();
		Convert.FromBase64String((string)response["result"]!["signedTx"]!).Length.ShouldBe(4);
	}

	// Spec §9.7: only "Ed25519" and "ECDSA" are signature kinds, written exactly so; any other value
	// is a bad param. A missing kind means Ed25519.
	[Theory]
	[InlineData("pha_sendTransaction", "\"ecdsa\"")]
	[InlineData("pha_sendTransaction", "\"None\"")]
	[InlineData("pha_signTransaction", "\"1\"")]
	[InlineData("pha_signTransaction", "\"\"")]
	public void An_unknown_signature_kind_gets_invalid_params(string method, string kind)
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);

		var response = Send(link,
			$"{{\"plv\":5,\"id\":\"k1\",\"session\":\"{session}\",\"method\":\"{method}\",\"params\":{{\"format\":\"script\",\"tx\":\"AAEC\",\"signatureKind\":{kind}}}}}");

		((int)response["error"]!["code"]!).ShouldBe(-32602);
		ops.SentFormat.ShouldBeNull();
		ops.SignTxFormat.ShouldBeNull();
	}

	// Spec §9.5: Carbon witnesses are Ed25519 only, so ECDSA with the carbon format gets 5003 before
	// the wallet is asked; with the script format the kind reaches the wallet.
	[Fact]
	public void ECDSA_is_refused_for_a_carbon_transaction_and_passed_on_for_a_script_one()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);

		var carbon = Send(link,
			$"{{\"plv\":5,\"id\":\"k2\",\"session\":\"{session}\",\"method\":\"pha_signTransaction\",\"params\":{{\"format\":\"carbon\",\"tx\":\"AAEC\",\"signatureKind\":\"ECDSA\"}}}}");
		((int)carbon["error"]!["code"]!).ShouldBe(5003);
		ops.SignTxFormat.ShouldBeNull();

		var script = Send(link,
			$"{{\"plv\":5,\"id\":\"k3\",\"session\":\"{session}\",\"method\":\"pha_signTransaction\",\"params\":{{\"format\":\"script\",\"tx\":\"AAEC\",\"signatureKind\":\"ECDSA\"}}}}");
		script["error"].ShouldBeNull();
		ops.SignTxKind.ShouldBe(SignatureKind.ECDSA);
	}

	// Spec §9.5: a wallet signs messages with the Ed25519 key only. "ECDSA" gets 5003, any other
	// value -32602, and "Ed25519" is accepted.
	[Theory]
	[InlineData("\"ECDSA\"", 5003)]
	[InlineData("\"RSA\"", -32602)]
	[InlineData("\"ed25519\"", -32602)]
	public void A_message_is_signed_with_the_Ed25519_key_only(string kind, int code)
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);

		var refused = Send(link,
			$"{{\"plv\":5,\"id\":\"s1\",\"session\":\"{session}\",\"method\":\"pha_signMessage\",\"params\":{{\"message\":\"AA==\",\"signatureKind\":{kind}}}}}");
		((int)refused["error"]!["code"]!).ShouldBe(code);
		ops.SignedMessage.ShouldBeNull();

		var accepted = Send(link,
			$"{{\"plv\":5,\"id\":\"s2\",\"session\":\"{session}\",\"method\":\"pha_signMessage\",\"params\":{{\"message\":\"AA==\",\"signatureKind\":\"Ed25519\"}}}}");
		accepted["error"].ShouldBeNull();
	}

	[Fact]
	public void Connect_rejection_maps_to_user_rejected_and_leaves_no_session()
	{
		var ops = new FakeV5Ops { RejectConnect = true };
		var link = new WalletLinkV5(ops);

		var response = Send(link,
			"{\"plv\":5,\"id\":\"c1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"d\",\"url\":\"https://d.example\"}}}");
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

		var failed = Send(link, "{\"plv\":5,\"id\":\"c1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"d\",\"url\":\"https://d.example\"}}}");
		((int)failed["error"]!["code"]!).ShouldBe(-32603);

		ops.FailConnect = LinkFailure.None;
		var ok = Send(link, "{\"plv\":5,\"id\":\"c2\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"d\",\"url\":\"https://d.example\"}}}");
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

	// Spec §7: a locked wallet never opens a second session in place of a live one. Over loopback
	// the resume of a live session gets 4900 and no prompt; a connect without a session still gets
	// the approval, where the user unlocks the wallet.
	[Fact]
	public void A_resume_while_the_wallet_is_locked_opens_no_second_session()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);
		var prompts = ops.ConnectCalls;

		ops.StatusValue = WalletStatus.Closed;
		var resume = Send(link,
			$"{{\"plv\":5,\"id\":\"r7\",\"session\":\"{session}\",\"method\":\"pha_connect\",\"params\":{{\"dapp\":{{\"name\":\"testdapp\",\"url\":\"https://t.example\"}}}}}}");

		((int)resume["error"]!["code"]!).ShouldBe(4900);
		ops.ConnectCalls.ShouldBe(prompts);
		link.IsSessionLive(session).ShouldBeTrue();
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
	public void Capabilities_advertise_the_size_limit_of_every_transport()
	{
		// Spec sections 5 and 11: one size per transport. The wallet side serves loopback,
		// the relay and deeplinks; loopback and the relay carry the 32 MiB chain maximum, a
		// deeplink a 3 KiB transaction, the largest that fits the 8192-character URL budget.
		var link = new WalletLinkV5(new FakeV5Ops());
		Connect(link, out var response);

		var sizes = (JObject)response["result"]!["capabilities"]!["maxPayloadBytes"]!;
		((int)sizes["loopback"]!).ShouldBe(32 * 1024 * 1024);
		((int)sizes["relay"]!).ShouldBe(32 * 1024 * 1024);
		((int)sizes["deeplink"]!).ShouldBe(3072);
	}

	[Fact]
	public void Capabilities_list_the_size_limit_of_the_served_transports_only()
	{
		// Spec section 11: a wallet lists only the transports it serves.
		var options = new WalletLinkV5Options { ServedTransports = new[] { LinkTransportKind.Loopback } };
		var link = new WalletLinkV5(new FakeV5Ops(), null, options);
		Connect(link, out var response);

		var sizes = (JObject)response["result"]!["capabilities"]!["maxPayloadBytes"]!;
		sizes.Properties().Select(p => p.Name).ShouldBe(new[] { "loopback" });
	}

	[Fact]
	public void Connect_result_has_no_feature_and_a_session_without_a_fixed_end()
	{
		// Spec section 5: `features` and `session.expiresAt` are required. This version defines no
		// feature, and a session lasts until the user ends it, so `expiresAt` is null.
		var link = new WalletLinkV5(new FakeV5Ops());
		Connect(link, out var response);

		var features = response["result"]!["capabilities"]!["features"];
		features.ShouldBeOfType<JArray>();
		((JArray)features!).Count.ShouldBe(0);
		var session = (JObject)response["result"]!["session"]!;
		session.ContainsKey("expiresAt").ShouldBeTrue();
		session["expiresAt"]!.Type.ShouldBe(JTokenType.Null);
	}

	[Fact]
	public void Connect_grants_the_requested_networks_and_methods_the_wallet_serves()
	{
		// Spec section 9.5: the wallet returns the granted capabilities, which may be a subset of
		// what was requested, and keeps them with the session: the resume and pha_getWalletInfo
		// return the same.
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);

		var connect = Send(link,
			"{\"plv\":5,\"id\":\"g1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"testdapp\",\"url\":\"https://t.example\"},"
			+ "\"chains\":[\"phantasma:mainnet\",\"phantasma:localnet\"],\"methods\":[\"pha_signMessage\",\"pha_unknown\"],\"features\":[\"x\"]}}");
		var session = (string)connect["result"]!["session"]!["id"]!;
		var resumed = Send(link,
			$"{{\"plv\":5,\"id\":\"g2\",\"session\":\"{session}\",\"method\":\"pha_connect\",\"params\":{{\"dapp\":{{\"name\":\"testdapp\",\"url\":\"https://t.example\"}}}}}}");
		var info = Send(link, $"{{\"plv\":5,\"id\":\"g3\",\"session\":\"{session}\",\"method\":\"pha_getWalletInfo\"}}");

		foreach (var capabilities in new[] { connect["result"]!["capabilities"]!, resumed["result"]!["capabilities"]!, info["result"]!["capabilities"]! })
		{
			((JArray)capabilities["chains"]!).Select(t => (string)t!).ShouldBe(new[] { "phantasma:localnet" });
			((JArray)capabilities["methods"]!).Select(t => (string)t!).ShouldBe(new[] { "pha_signMessage" });
			((JArray)capabilities["features"]!).Count.ShouldBe(0);
		}
		ops.ConnectCalls.ShouldBe(1);
	}

	[Fact]
	public void A_connect_for_networks_the_wallet_does_not_serve_gets_4902_without_a_prompt()
	{
		// Spec section 10: 4902 when the wallet serves none of the requested networks.
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);

		var response = Send(link,
			"{\"plv\":5,\"id\":\"u1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"testdapp\",\"url\":\"https://t.example\"},\"chains\":[\"phantasma:mainnet\"]}}");

		((int)response["error"]!["code"]!).ShouldBe(4902);
		ops.ConnectCalls.ShouldBe(0);
	}

	[Fact]
	public void A_session_stored_without_grants_gets_everything_the_wallet_serves()
	{
		// A session stored by an older version has no grants: it keeps every network and method
		// the wallet serves.
		var store = new InMemoryLinkSessionStore();
		store.Save(new LinkSessionRecord
		{
			Id = "old",
			Dapp = "testdapp",
			DappUrl = "https://t.example",
			Address = "P2KTest",
			CreatedUtc = DateTime.UtcNow,
			LastSeenUtc = DateTime.UtcNow,
		});
		var link = new WalletLinkV5(new FakeV5Ops(), store);

		var info = Send(link, "{\"plv\":5,\"id\":\"o1\",\"session\":\"old\",\"method\":\"pha_getWalletInfo\"}");

		var capabilities = info["result"]!["capabilities"]!;
		((JArray)capabilities["chains"]!).Select(t => (string)t!).ShouldBe(new[] { "phantasma:localnet" });
		((JArray)capabilities["methods"]!).Select(t => (string)t!).ShouldContain("pha_sendTransaction");
	}

	[Fact]
	public void GetWalletInfo_returns_the_capabilities_of_the_connect_result()
	{
		// Spec section 9.5: pha_getWalletInfo returns { name, version, capabilities, rpc }, and
		// the capabilities are the same object the connect result carries.
		var link = new WalletLinkV5(new FakeV5Ops());
		var session = Connect(link, out var connect);

		var info = Send(link, $"{{\"plv\":5,\"id\":\"w1\",\"session\":\"{session}\",\"method\":\"pha_getWalletInfo\"}}");

		((string)info["result"]!["name"]!).ShouldBe("FakeWallet");
		JToken.DeepEquals(info["result"]!["capabilities"], connect["result"]!["capabilities"]).ShouldBeTrue();
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

		// Spec §9.5: each result is one VM object as the chain serializes it, in base64.
		var ok = Send(link,
			$"{{\"plv\":5,\"id\":\"i1\",\"session\":\"{session}\",\"method\":\"pha_invokeScript\",\"params\":{{\"chain\":\"main\",\"script\":\"AA==\"}}}}");
		((JArray)ok["result"]!["results"]!).Single().Value<string>().ShouldBe(Convert.ToBase64String(new byte[] { 0xAA, 0xBB }));

		ops.FailInvoke = LinkFailure.Internal;
		var fail = Send(link,
			$"{{\"plv\":5,\"id\":\"i2\",\"session\":\"{session}\",\"method\":\"pha_invokeScript\",\"params\":{{\"chain\":\"main\",\"script\":\"AA==\"}}}}");
		((int)fail["error"]!["code"]!).ShouldBe(-32603);
	}

	// Spec §9.5: `chain` and `script` are required, and a script above the chain's maximum size is
	// refused with 5001 before it is decoded. None of these reaches the wallet's node.
	[Theory]
	[InlineData("{\"script\":\"AA==\"}", -32602)]
	[InlineData("{\"chain\":\"\",\"script\":\"AA==\"}", -32602)]
	[InlineData("{\"chain\":\"main\"}", -32602)]
	[InlineData("{\"chain\":\"main\",\"script\":\"\"}", -32602)]
	[InlineData("{\"chain\":\"main\",\"script\":\"%%%\"}", -32602)]
	public void InvokeScript_requires_the_chain_and_a_base64_script(string prms, int code)
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);

		var response = Send(link,
			$"{{\"plv\":5,\"id\":\"i3\",\"session\":\"{session}\",\"method\":\"pha_invokeScript\",\"params\":{prms}}}");

		((int)response["error"]!["code"]!).ShouldBe(code);
		ops.InvokeCalls.ShouldBe(0);
	}

	[Fact]
	public void InvokeScript_refuses_a_script_above_the_maximum_size()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);
		// One base64 group beyond the base64 length of the chain's 32 MiB maximum.
		var huge = new string('A', (32 * 1024 * 1024 + 2) / 3 * 4 + 4);

		var response = Send(link,
			$"{{\"plv\":5,\"id\":\"i4\",\"session\":\"{session}\",\"method\":\"pha_invokeScript\",\"params\":{{\"chain\":\"main\",\"script\":\"{huge}\"}}}}");

		((int)response["error"]!["code"]!).ShouldBe(5001);
		((int)response["error"]!["data"]!["maxPayloadBytes"]!).ShouldBe(32 * 1024 * 1024);
		ops.InvokeCalls.ShouldBe(0);
	}

	// Spec §7, §9.5: every approval shows the dApp, its origin and the account. The connect prompt
	// gets the dApp of the request and the channel's origin and transport.
	[Fact]
	public void Connect_gives_the_wallet_the_dApp_and_the_origin_of_the_request()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var bound = new List<string>();
		var context = new LinkRequestContext { Origin = "https://page.example", OnSession = bound.Add };

		var response = Send(link,
			"{\"plv\":5,\"id\":\"o1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"Game\",\"url\":\"https://game.example\",\"icon\":\"https://game.example/i.png\"}}}",
			context);

		var caller = ops.LastCaller!;
		caller.DappName.ShouldBe("Game");
		caller.DappUrl.ShouldBe("https://game.example");
		caller.DappIcon.ShouldBe("https://game.example/i.png");
		caller.Origin.ShouldBe("https://page.example");
		caller.Transport.ShouldBe(LinkTransportKind.Loopback);
		bound.ShouldBe(new[] { (string)response["result"]!["session"]!["id"]! });
	}

	// Spec §7: the wallet signs for a session with its account only, and shows the session's dApp
	// in the prompt. A sign operation gets the session's dApp, account and origin.
	[Fact]
	public void A_sign_operation_gets_the_dApp_and_the_account_of_the_session()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var context = new LinkRequestContext { Origin = "https://page.example" };
		var session = (string)Send(link,
			"{\"plv\":5,\"id\":\"o1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"Game\",\"url\":\"https://game.example\"}}}",
			context)["result"]!["session"]!["id"]!;

		Send(link, $"{{\"plv\":5,\"id\":\"o2\",\"session\":\"{session}\",\"method\":\"pha_signMessage\",\"params\":{{\"message\":\"AA==\"}}}}", context);

		var caller = ops.LastCaller!;
		caller.DappName.ShouldBe("Game");
		caller.DappUrl.ShouldBe("https://game.example");
		caller.Address.ShouldBe("P2KTest");
		caller.Origin.ShouldBe("https://page.example");
	}

	// Spec §7: a session is served only on the channel it was made on. A session bound to an origin
	// gets 4100 from another origin and from a caller without one, and reaches no prompt.
	[Fact]
	public void A_session_bound_to_an_origin_serves_that_origin_only()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = (string)Send(link,
			"{\"plv\":5,\"id\":\"o1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"Game\",\"url\":\"https://game.example\"}}}",
			new LinkRequestContext { Origin = "https://page.example" })["result"]!["session"]!["id"]!;
		var request = $"{{\"plv\":5,\"id\":\"o2\",\"session\":\"{session}\",\"method\":\"pha_signMessage\",\"params\":{{\"message\":\"AA==\"}}}}";

		var other = Send(link, request, new LinkRequestContext { Origin = "https://evil.example" });
		((int)other["error"]!["code"]!).ShouldBe(4100);
		ops.SignedMessage.ShouldBeNull();

		var unknown = Send(link, request, new LinkRequestContext());
		((int)unknown["error"]!["code"]!).ShouldBe(4100);
		ops.SignedMessage.ShouldBeNull();

		var own = Send(link, request.Replace("\"o2\"", "\"o3\""), new LinkRequestContext { Origin = "https://page.example" });
		own["error"].ShouldBeNull();
	}

	// Spec §7: over deeplink and relay the channel is the pairing, which carries one session. A
	// request that names another session gets 4100, and no connect moves the pairing to it. A
	// resume over the pairing is decided by the pairing alone, whatever dApp name it carries.
	[Fact]
	public void A_pairing_serves_its_own_session_only()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var other = Connect(link, out _);
		var own = Connect(link, out _);
		var moved = new List<string>();
		LinkRequestContext Pairing() => new LinkRequestContext { Transport = LinkTransportKind.Relay, PairingSession = own, Channel = "pairing p", OnSession = moved.Add };

		var foreign = Send(link, $"{{\"plv\":5,\"id\":\"p1\",\"session\":\"{other}\",\"method\":\"pha_getChains\"}}", Pairing());
		((int)foreign["error"]!["code"]!).ShouldBe(4100);
		var repoint = Send(link, $"{{\"plv\":5,\"id\":\"p2\",\"session\":\"{other}\",\"method\":\"pha_connect\",\"params\":{{\"dapp\":{{\"name\":\"testdapp\",\"url\":\"https://t.example\"}}}}}}", Pairing());
		((int)repoint["error"]!["code"]!).ShouldBe(4100);
		moved.ShouldBeEmpty();

		var resume = Send(link, "{\"plv\":5,\"id\":\"p3\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"Renamed\",\"url\":\"https://t.example\"}}}", Pairing());
		((string)resume["result"]!["session"]!["id"]!).ShouldBe(own);
		var served = Send(link, $"{{\"plv\":5,\"id\":\"p4\",\"session\":\"{own}\",\"method\":\"pha_getChains\"}}", Pairing());
		served["error"].ShouldBeNull();
	}

	// Spec §4: requests are independent by id, and there is no global single-in-flight flag. A
	// prompt that is still open does not block another request, and each answer carries the id of
	// its own request, in whatever order the user answers.
	[Fact]
	public void Each_pending_request_is_answered_by_its_own_id()
	{
		var ops = new FakeV5Ops { DeferConnect = true };
		var link = new WalletLinkV5(ops);

		JObject? first = null;
		JObject? second = null;
		link.HandleMessage(
			"{\"plv\":5,\"id\":\"c1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"a\",\"url\":\"https://a.example\"}}}",
			s => first = JObject.Parse(s));
		link.HandleMessage(
			"{\"plv\":5,\"id\":\"c2\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"b\",\"url\":\"https://b.example\"}}}",
			s => second = JObject.Parse(s));
		first.ShouldBeNull();
		second.ShouldBeNull();
		ops.PendingConnects.Count.ShouldBe(2);

		ops.PendingConnects[1](LinkConnectResult.Fail(LinkFailure.UserRejected, "rejected"));
		((string)second!["id"]!).ShouldBe("c2");
		((int)second["error"]!["code"]!).ShouldBe(4001);
		first.ShouldBeNull();

		ops.PendingConnects[0](LinkConnectResult.Ok(new LinkAccount { Address = "P2KTest", Name = "tester" }, "FakeWallet", "9.9.9", "localnet"));
		((string)first!["id"]!).ShouldBe("c1");
		((string)first["result"]!["session"]!["id"]!).ShouldNotBeNullOrEmpty();
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

	// Spec §4: valid JSON that is no request object, or a request without a string id, gets -32600.
	// The answer carries a null id, because nothing in the message can correlate it.
	[Theory]
	[InlineData("[]")]
	[InlineData("\"text\"")]
	[InlineData("5")]
	[InlineData("{\"plv\":5,\"method\":\"pha_getChains\"}")]
	[InlineData("{\"plv\":5,\"id\":7,\"method\":\"pha_getChains\"}")]
	[InlineData("{\"plv\":5,\"id\":{},\"method\":\"pha_getChains\"}")]
	[InlineData("{\"plv\":5,\"id\":\"\",\"method\":\"pha_getChains\"}")]
	public void A_message_without_a_request_object_or_a_string_id_gets_invalid_request(string message)
	{
		var response = Send(new WalletLinkV5(new FakeV5Ops()), message);

		((int)response["error"]!["code"]!).ShouldBe(-32600);
		response["id"]!.Type.ShouldBe(JTokenType.Null);
	}

	// Spec §4: an envelope field of the wrong type is an invalid request (-32600), a params value
	// that is no object is a bad param (-32602), and the answer keeps the request id.
	[Theory]
	[InlineData("{\"plv\":\"5\",\"id\":\"e1\",\"method\":\"pha_getChains\"}", -32600)]
	[InlineData("{\"plv\":4,\"id\":\"e1\",\"method\":\"pha_getChains\"}", -32600)]
	[InlineData("{\"id\":\"e1\",\"method\":\"pha_getChains\"}", -32600)]
	[InlineData("{\"plv\":5,\"id\":\"e1\",\"method\":{}}", -32600)]
	[InlineData("{\"plv\":5,\"id\":\"e1\",\"method\":\"\"}", -32600)]
	[InlineData("{\"plv\":5,\"id\":\"e1\",\"method\":\"pha_getChains\",\"session\":{}}", -32600)]
	[InlineData("{\"plv\":5,\"id\":\"e1\",\"method\":\"pha_getChains\",\"sentAt\":\"now\"}", -32600)]
	[InlineData("{\"plv\":5,\"id\":\"e1\",\"method\":\"pha_getChains\",\"params\":[]}", -32602)]
	public void An_envelope_field_of_the_wrong_type_gets_an_error_with_the_request_id(string message, int code)
	{
		var response = Send(new WalletLinkV5(new FakeV5Ops()), message);

		((int)response["error"]!["code"]!).ShouldBe(code);
		((string)response["id"]!).ShouldBe("e1");
	}

	// Spec §4: a field of a request set to null counts as absent, so a null send time is no error.
	[Fact]
	public void A_request_field_set_to_null_counts_as_absent()
	{
		var response = Send(new WalletLinkV5(new FakeV5Ops()),
			"{\"plv\":5,\"id\":\"n1\",\"method\":\"pha_getChains\",\"session\":null,\"sentAt\":null,\"params\":null}");

		((int)response["error"]!["code"]!).ShouldBe(4100);
		((string)response["id"]!).ShouldBe("n1");
	}

	// Spec §4: a method that the wallet does not implement gets -32601 before the checks of the
	// session and of the lock, because the methods of a wallet depend on neither.
	[Fact]
	public void An_unknown_method_gets_method_not_found_before_the_session_and_lock_checks()
	{
		var ops = new FakeV5Ops { StatusValue = WalletStatus.Closed };
		var link = new WalletLinkV5(ops);

		var noSession = Send(link, "{\"plv\":5,\"id\":\"m1\",\"method\":\"pha_requestSigningPermission\",\"params\":{}}");
		((int)noSession["error"]!["code"]!).ShouldBe(-32601);
		var badSession = Send(link, "{\"plv\":5,\"id\":\"m2\",\"session\":\"gone\",\"method\":\"pha_unknown\"}");
		((int)badSession["error"]!["code"]!).ShouldBe(-32601);
	}

	// Spec §4: params of the wrong JSON type are bad params (-32602). None of them may throw out of
	// HandleMessage: the request is answered, and the wallet is never asked.
	[Theory]
	[InlineData("pha_connect", "{\"dapp\":{\"name\":{}}}")]
	[InlineData("pha_connect", "{\"dapp\":{\"name\":7}}")]
	[InlineData("pha_connect", "{\"dapp\":{\"name\":\"d\"}}")]
	[InlineData("pha_connect", "{\"dapp\":{\"name\":\"d\",\"url\":\"\"}}")]
	[InlineData("pha_connect", "{\"dapp\":{\"name\":\"d\",\"url\":7}}")]
	[InlineData("pha_connect", "{\"dapp\":{\"name\":\"d\",\"url\":\"https://d.example\",\"icon\":{}}}")]
	[InlineData("pha_connect", "{\"dapp\":{\"name\":\"d\",\"url\":\"https://d.example\"},\"chains\":\"phantasma:localnet\"}")]
	[InlineData("pha_connect", "{\"dapp\":{\"name\":\"d\",\"url\":\"https://d.example\"},\"methods\":[1]}")]
	[InlineData("pha_connect", "{\"dapp\":{\"name\":\"d\",\"url\":\"https://d.example\"},\"features\":{}}")]
	[InlineData("pha_sendTransaction", "{\"format\":7,\"tx\":\"AAEC\"}")]
	[InlineData("pha_sendTransaction", "{\"format\":\"carbon\",\"tx\":{}}")]
	[InlineData("pha_sendTransaction", "{\"format\":\"carbon\",\"tx\":\"AAEC\",\"signatureKind\":1}")]
	[InlineData("pha_signMessage", "{\"message\":{}}")]
	[InlineData("pha_signMessage", "{\"message\":\"AAEC\",\"display\":[]}")]
	[InlineData("pha_invokeScript", "{\"chain\":{},\"script\":\"AA==\"}")]
	[InlineData("pha_invokeScript", "{\"chain\":\"main\",\"script\":5}")]
	public void A_param_of_the_wrong_type_gets_invalid_params(string method, string prms)
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);
		var connectCalls = ops.ConnectCalls;

		var response = Send(link,
			$"{{\"plv\":5,\"id\":\"p1\",\"session\":\"{session}\",\"method\":\"{method}\",\"params\":{prms}}}");

		((int)response["error"]!["code"]!).ShouldBe(-32602);
		ops.ConnectCalls.ShouldBe(connectCalls);
		ops.SentTx.ShouldBeNull();
		ops.SignTxFormat.ShouldBeNull();
		ops.SignedMessage.ShouldBeNull();
	}

	// An exception from the wallet's own code ends as a -32603 answer. It must not reach the
	// transport, which would take its connection for dead.
	[Fact]
	public void An_exception_from_the_wallet_is_answered_with_internal_error()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);
		ops.ThrowOnGetChains = true;

		var response = Send(link, $"{{\"plv\":5,\"id\":\"x1\",\"session\":\"{session}\",\"method\":\"pha_getChains\"}}");

		((int)response["error"]!["code"]!).ShouldBe(-32603);
		((string)response["id"]!).ShouldBe("x1");
	}

	// A wallet callback that fires twice still produces one answer per request.
	[Fact]
	public void A_request_gets_one_answer_even_when_the_wallet_answers_twice()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);
		ops.AnswerTwice = true;

		var answers = 0;
		link.HandleMessage($"{{\"plv\":5,\"id\":\"t1\",\"session\":\"{session}\",\"method\":\"pha_getChains\"}}", _ => answers++);

		answers.ShouldBe(1);
	}
	// Spec §5, §7: `expiresAt` is the end of the lifetime the user picked, in Unix milliseconds, or
	// null for a lifetime without a fixed end.
	[Theory]
	[InlineData(LinkSessionLifetime.OneHour, 3600)]
	[InlineData(LinkSessionLifetime.OneDay, 86400)]
	[InlineData(LinkSessionLifetime.OneMonth, 30 * 86400)]
	[InlineData(LinkSessionLifetime.Session, 0)]
	[InlineData(LinkSessionLifetime.Always, 0)]
	public void The_connect_result_reports_the_end_of_the_lifetime_the_user_picked(LinkSessionLifetime lifetime, int seconds)
	{
		var link = new WalletLinkV5(new FakeV5Ops { ConnectLifetime = lifetime });
		var start = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		Connect(link, out var response);
		var end = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var expiresAt = response["result"]!["session"]!["expiresAt"]!;
		if (seconds == 0)
		{
			expiresAt.Type.ShouldBe(JTokenType.Null);
			return;
		}
		((long)expiresAt).ShouldBeInRange(start + seconds * 1000L, end + seconds * 1000L);
	}

	[Fact]
	public void A_request_in_a_session_past_its_end_gets_5100_and_ends_it()
	{
		// Spec §7: the session ends, its dApp gets pha_sessionExpired, and the transports hear of
		// the end. The wallet then no longer keeps it.
		var sessions = new InMemoryLinkSessionStore();
		var link = new WalletLinkV5(new FakeV5Ops(), sessions);
		var session = Connect(link, out _);
		var events = new List<JObject>();
		var ended = new List<(string, LinkSessionEndReason)>();
		link.SessionEvent += (_, envelope) => events.Add(JObject.Parse(envelope));
		link.SessionEnded += (id, reason) => ended.Add((id, reason));
		sessions.Get(session)!.ExpiresUtc = DateTime.UtcNow.AddSeconds(-1);

		var response = Send(link, $"{{\"plv\":5,\"id\":\"e1\",\"session\":\"{session}\",\"method\":\"pha_getChains\"}}");

		((int)response["error"]!["code"]!).ShouldBe(5100);
		var notice = events.Single();
		((string)notice["event"]!).ShouldBe("pha_sessionExpired");
		((string)notice["session"]!).ShouldBe(session);
		((string)notice["data"]!["session"]!).ShouldBe(session);
		ended.ShouldBe(new[] { (session, LinkSessionEndReason.Expired) });
		var after = Send(link, $"{{\"plv\":5,\"id\":\"e2\",\"session\":\"{session}\",\"method\":\"pha_getChains\"}}");
		((int)after["error"]!["code"]!).ShouldBe(4100);
	}

	[Fact]
	public void A_session_without_a_request_for_7_days_expires()
	{
		var sessions = new InMemoryLinkSessionStore();
		var link = new WalletLinkV5(new FakeV5Ops(), sessions);
		var session = Connect(link, out _);
		sessions.Get(session)!.LastSeenUtc = DateTime.UtcNow.AddDays(-7).AddMinutes(-1);

		var response = Send(link, $"{{\"plv\":5,\"id\":\"i1\",\"session\":\"{session}\",\"method\":\"pha_getChains\"}}");

		((int)response["error"]!["code"]!).ShouldBe(5100);
	}

	[Fact]
	public void Every_request_moves_the_inactivity_end()
	{
		var sessions = new InMemoryLinkSessionStore();
		var link = new WalletLinkV5(new FakeV5Ops(), sessions);
		var session = Connect(link, out _);
		sessions.Get(session)!.LastSeenUtc = DateTime.UtcNow.AddDays(-6);

		var response = Send(link, $"{{\"plv\":5,\"id\":\"a1\",\"session\":\"{session}\",\"method\":\"pha_getChains\"}}");

		response["error"].ShouldBeNull();
		sessions.Get(session)!.LastSeenUtc.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(-1));
	}

	[Fact]
	public void The_lock_ends_the_sessions_with_the_lifetime_session_only()
	{
		// Spec §7: the lifetime `session` ends when the wallet locks.
		var ops = new FakeV5Ops { ConnectLifetime = LinkSessionLifetime.Session };
		var link = new WalletLinkV5(ops);
		var shortSession = Connect(link, out _);
		ops.ConnectLifetime = LinkSessionLifetime.Always;
		var longSession = Connect(link, out _);
		var events = new List<JObject>();
		link.SessionEvent += (_, envelope) => events.Add(JObject.Parse(envelope));

		link.EndSessionLifetimes();

		events.Select(e => ((string)e["event"]!, (string)e["session"]!)).ShouldBe(new[] { ("pha_sessionExpired", shortSession) });
		link.IsSessionLive(shortSession).ShouldBeFalse();
		link.IsSessionLive(longSession).ShouldBeTrue();
	}

	[Fact]
	public void A_network_switch_reaches_every_live_session()
	{
		// Spec §9.5: pha_chainChanged carries the CAIP-2 id of the network.
		var sessions = new InMemoryLinkSessionStore();
		var link = new WalletLinkV5(new FakeV5Ops(), sessions);
		var first = Connect(link, out _);
		var second = Connect(link, out _);
		var expired = Connect(link, out _);
		sessions.Get(expired)!.ExpiresUtc = DateTime.UtcNow.AddSeconds(-1);
		var events = new List<(string, JObject)>();
		link.SessionEvent += (session, envelope) => events.Add((session, JObject.Parse(envelope)));

		link.ChainChanged();

		events.Select(e => e.Item1).OrderBy(id => id).ShouldBe(new[] { first, second }.OrderBy(id => id));
		events.ShouldAllBe(e => (string?)e.Item2["event"] == "pha_chainChanged" && (string?)e.Item2["data"]!["chain"] == "phantasma:localnet");
	}

	[Fact]
	public void Disconnect_succeeds_before_the_lock_and_session_checks_and_ends_the_session()
	{
		// Spec §9.5: pha_disconnect is idempotent; the session it ends gets pha_sessionDeleted.
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);
		var events = new List<JObject>();
		link.SessionEvent += (_, envelope) => events.Add(JObject.Parse(envelope));
		ops.StatusValue = WalletStatus.Closed;

		var unknown = Send(link, "{\"plv\":5,\"id\":\"d0\",\"session\":\"gone\",\"method\":\"pha_disconnect\"}");
		var known = Send(link, $"{{\"plv\":5,\"id\":\"d1\",\"session\":\"{session}\",\"method\":\"pha_disconnect\"}}");

		((bool)unknown["result"]!["ok"]!).ShouldBeTrue();
		((bool)known["result"]!["ok"]!).ShouldBeTrue();
		var notice = events.Last();
		((string)notice["event"]!).ShouldBe("pha_sessionDeleted");
		((string)notice["session"]!).ShouldBe(session);
		link.IsSessionLive(session).ShouldBeFalse();
	}

	// Spec §7, §9.5: a live session of another channel is neither unknown nor closed, so a disconnect
	// that names it gets 4100 and leaves it.
	[Fact]
	public void A_disconnect_from_another_origin_gets_4100_and_leaves_the_session()
	{
		var link = new WalletLinkV5(new FakeV5Ops());
		var session = (string)Send(link,
			"{\"plv\":5,\"id\":\"c1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"testdapp\",\"url\":\"https://t.example\"}}}",
			new LinkRequestContext { Origin = "https://page.example" })["result"]!["session"]!["id"]!;

		var response = Send(link, $"{{\"plv\":5,\"id\":\"d1\",\"session\":\"{session}\",\"method\":\"pha_disconnect\"}}",
			new LinkRequestContext { Origin = "https://other.example" });

		((int)response["error"]!["code"]!).ShouldBe(4100);
		link.IsSessionLive(session).ShouldBeTrue();
	}

	// Spec §9.5: a session of another channel that has ended is no live session there, so its
	// disconnect is a success no-op, as for an unknown one.
	[Fact]
	public void A_disconnect_from_another_origin_of_an_ended_session_succeeds()
	{
		var sessions = new InMemoryLinkSessionStore();
		var link = new WalletLinkV5(new FakeV5Ops(), sessions);
		var session = (string)Send(link,
			"{\"plv\":5,\"id\":\"c1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"testdapp\",\"url\":\"https://t.example\"}}}",
			new LinkRequestContext { Origin = "https://page.example" })["result"]!["session"]!["id"]!;
		sessions.Get(session)!.ExpiresUtc = DateTime.UtcNow.AddSeconds(-1);

		var response = Send(link, $"{{\"plv\":5,\"id\":\"d1\",\"session\":\"{session}\",\"method\":\"pha_disconnect\"}}",
			new LinkRequestContext { Origin = "https://other.example" });

		((bool)response["result"]!["ok"]!).ShouldBeTrue();
		// The request ends nothing: the sweep ends the session as expired, on its own channel.
		sessions.Get(session).ShouldNotBeNull();
	}

	// Spec §7: a session made over a pairing is served over that pairing only, never over loopback,
	// whatever origin the loopback caller names.
	[Fact]
	public void A_session_of_a_pairing_is_not_served_over_loopback()
	{
		var link = new WalletLinkV5(new FakeV5Ops());
		var session = (string)Send(link,
			"{\"plv\":5,\"id\":\"c1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"testdapp\",\"url\":\"https://t.example\"}}}",
			new LinkRequestContext { Transport = LinkTransportKind.Relay, Origin = "https://t.example", Channel = "pairing p" })["result"]!["session"]!["id"]!;

		foreach (var origin in new[] { "https://t.example", null })
		{
			var chains = Send(link, $"{{\"plv\":5,\"id\":\"g-{origin}\",\"session\":\"{session}\",\"method\":\"pha_getChains\"}}",
				new LinkRequestContext { Transport = LinkTransportKind.Loopback, Origin = origin });
			((int)chains["error"]!["code"]!).ShouldBe(4100);
		}
		var served = Send(link, $"{{\"plv\":5,\"id\":\"g2\",\"session\":\"{session}\",\"method\":\"pha_getChains\"}}",
			new LinkRequestContext { Transport = LinkTransportKind.Relay, PairingSession = session, Channel = "pairing p" });
		served["result"].ShouldNotBeNull();
	}

	// Spec §18.2: the connect that made a session of a pairing joins the ids of that session, so a
	// copy of it that the relay hands over again resumes nothing and gets no second answer.
	[Fact]
	public void A_copy_of_the_first_connect_of_a_pairing_gets_no_second_answer()
	{
		var link = new WalletLinkV5(new FakeV5Ops());
		var connect = "{\"plv\":5,\"id\":\"c-first\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"testdapp\",\"url\":\"https://t.example\"}}}";
		var answers = new List<string>();
		link.HandleMessage(connect, answers.Add, new LinkRequestContext { Transport = LinkTransportKind.Relay, Channel = "pairing p" });
		var session = (string)JObject.Parse(answers.Single())["result"]!["session"]!["id"]!;

		link.HandleMessage(connect, answers.Add,
			new LinkRequestContext { Transport = LinkTransportKind.Relay, PairingSession = session, Channel = "pairing p" });

		answers.Count.ShouldBe(1);
	}

	// Spec §7: a connect from another origin that names an expired session neither ends it nor
	// opens a new one in its place: it gets 4100 like any request from another channel.
	[Fact]
	public void A_connect_from_another_origin_naming_an_expired_session_gets_4100()
	{
		var sessions = new InMemoryLinkSessionStore();
		var link = new WalletLinkV5(new FakeV5Ops(), sessions);
		var session = (string)Send(link,
			"{\"plv\":5,\"id\":\"c1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"testdapp\",\"url\":\"https://t.example\"}}}",
			new LinkRequestContext { Origin = "https://page.example" })["result"]!["session"]!["id"]!;
		sessions.Get(session)!.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
		var ended = new List<string>();
		link.SessionEnded += (id, _) => ended.Add(id);

		var response = Send(link,
			$"{{\"plv\":5,\"id\":\"c2\",\"session\":\"{session}\",\"method\":\"pha_connect\",\"params\":{{\"dapp\":{{\"name\":\"testdapp\",\"url\":\"https://t.example\"}}}}}}",
			new LinkRequestContext { Origin = "https://other.example" });

		((int)response["error"]!["code"]!).ShouldBe(4100);
		ended.ShouldBeEmpty();
		sessions.Get(session).ShouldNotBeNull();
	}

	[Fact]
	public void A_resume_over_loopback_of_an_expired_session_runs_the_approval_again()
	{
		// Spec §9.5: over loopback a session that ended gets the full approval and a new session.
		var sessions = new InMemoryLinkSessionStore();
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops, sessions);
		var session = Connect(link, out _);
		sessions.Get(session)!.ExpiresUtc = DateTime.UtcNow.AddSeconds(-1);

		var response = Send(link,
			$"{{\"plv\":5,\"id\":\"r1\",\"session\":\"{session}\",\"method\":\"pha_connect\",\"params\":{{\"dapp\":{{\"name\":\"testdapp\",\"url\":\"https://t.example\"}}}}}}");

		ops.ConnectCalls.ShouldBe(2);
		((string)response["result"]!["session"]!["id"]!).ShouldNotBe(session);
	}

	[Fact]
	public void A_session_past_the_limit_evicts_the_least_recently_used_one()
	{
		// Spec §7: the store is bounded; the evicted session's dApp gets pha_sessionDeleted.
		var sessions = new InMemoryLinkSessionStore();
		var link = new WalletLinkV5(new FakeV5Ops(), sessions, new WalletLinkV5Options { MaxSessions = 2 });
		var oldest = Connect(link, out _);
		var second = Connect(link, out _);
		sessions.Get(oldest)!.LastSeenUtc = DateTime.UtcNow.AddMinutes(-5);
		var ended = new List<(string, LinkSessionEndReason)>();
		link.SessionEnded += (id, reason) => ended.Add((id, reason));

		var third = Connect(link, out _);

		ended.ShouldBe(new[] { (oldest, LinkSessionEndReason.Evicted) });
		link.IsSessionLive(second).ShouldBeTrue();
		link.IsSessionLive(third).ShouldBeTrue();
	}

	/// <summary>A session store that keeps each record as JSON, as the durable store of a wallet
	/// does: what the dispatcher keeps with a session must survive a write and a read.</summary>
	private sealed class JsonSessionStore : ILinkSessionStore
	{
		private readonly Dictionary<string, string> _json = new Dictionary<string, string>();

		public LinkSessionRecord? Get(string id) =>
			_json.TryGetValue(id, out var json) ? JsonConvert.DeserializeObject<LinkSessionRecord>(json) : null;
		public void Save(LinkSessionRecord record) => _json[record.Id] = JsonConvert.SerializeObject(record);
		public void Remove(string id) => _json.Remove(id);
		public LinkSessionRecord[] List() =>
			_json.Values.Select(json => JsonConvert.DeserializeObject<LinkSessionRecord>(json)!).ToArray();
	}

	private static string InvokeRequest(string session, string id) =>
		$"{{\"plv\":5,\"id\":\"{id}\",\"session\":\"{session}\",\"method\":\"pha_invokeScript\",\"params\":{{\"chain\":\"main\",\"script\":\"AA==\"}}}}";

	// Spec §18.2: the wallet handles each request id of a session once. A copy gets no answer,
	// because the first copy gets it.
	[Fact]
	public void A_copy_of_a_request_in_a_session_is_dropped_without_an_answer()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);
		var answers = new List<string>();

		link.HandleMessage(InvokeRequest(session, "q1"), answers.Add);
		link.HandleMessage(InvokeRequest(session, "q1"), answers.Add);

		answers.Count.ShouldBe(1);
		ops.InvokeCalls.ShouldBe(1);
	}

	[Fact]
	public void A_request_id_is_kept_for_10_minutes()
	{
		var sessions = new InMemoryLinkSessionStore();
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops, sessions);
		var session = Connect(link, out _);
		Send(link, InvokeRequest(session, "q1"));
		// The connect that made the session is among its ids too (spec §18.2).
		var seen = sessions.Get(session)!.SeenRequests.Single(request => request.Id == "q1");
		seen.UntilUtc.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(9));

		seen.UntilUtc = DateTime.UtcNow.AddSeconds(-1);
		var again = Send(link, InvokeRequest(session, "q1"));

		again["result"].ShouldNotBeNull();
		ops.InvokeCalls.ShouldBe(2);
	}

	[Fact]
	public void The_id_memory_and_the_session_survive_a_restart_of_the_wallet()
	{
		// Spec §18.2: the relay hands the messages it holds to the first subscription after a
		// restart, so the ids are stored with the session.
		var store = new JsonSessionStore();
		var ops = new FakeV5Ops { ConnectLifetime = LinkSessionLifetime.OneDay };
		var session = Connect(new WalletLinkV5(ops, store), out _);
		Send(new WalletLinkV5(ops, store), InvokeRequest(session, "q1"));

		var answers = 0;
		new WalletLinkV5(ops, store).HandleMessage(InvokeRequest(session, "q1"), _ => answers++);

		answers.ShouldBe(0);
		ops.InvokeCalls.ShouldBe(1);
		var record = store.Get(session)!;
		record.Lifetime.ShouldBe(LinkSessionLifetime.OneDay);
		record.ExpiresUtc.ShouldNotBeNull();
		record.Chains.ShouldBe(new[] { "phantasma:localnet" });
	}

	[Fact]
	public void A_session_that_holds_the_most_ids_gets_its_new_requests_refused()
	{
		// Spec §18.2: a wallet never forgets an id early because it holds many.
		var sessions = new InMemoryLinkSessionStore();
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops, sessions);
		var session = Connect(link, out _);
		var record = sessions.Get(session)!;
		for (var i = 0; i < 1024; i++)
		{
			record.SeenRequests.Add(new LinkSeenRequest { Id = "old" + i, UntilUtc = DateTime.UtcNow.AddMinutes(5) });
		}

		var response = Send(link, InvokeRequest(session, "new1"));

		((int)response["error"]!["code"]!).ShouldBe(-32603);
		ops.InvokeCalls.ShouldBe(0);
		var answers = 0;
		link.HandleMessage(InvokeRequest(session, "old0"), _ => answers++);
		answers.ShouldBe(0);
	}

	[Fact]
	public void A_copy_of_a_resume_is_dropped()
	{
		var link = new WalletLinkV5(new FakeV5Ops());
		var session = Connect(link, out _);
		var resume = $"{{\"plv\":5,\"id\":\"r9\",\"session\":\"{session}\",\"method\":\"pha_connect\",\"params\":{{\"dapp\":{{\"name\":\"testdapp\",\"url\":\"https://t.example\"}}}}}}";
		var answers = new List<string>();

		link.HandleMessage(resume, answers.Add);
		link.HandleMessage(resume, answers.Add);

		answers.Count.ShouldBe(1);
	}

	[Fact]
	public void A_copy_of_a_connect_that_waits_for_the_user_is_dropped()
	{
		// A connect without a session has no id memory yet. A relay re-send of it must not open a
		// second prompt and a second session.
		var ops = new FakeV5Ops { DeferConnect = true };
		var link = new WalletLinkV5(ops);
		var connect = "{\"plv\":5,\"id\":\"c7\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"testdapp\",\"url\":\"https://t.example\"}}}";
		var answers = new List<string>();

		link.HandleMessage(connect, answers.Add);
		link.HandleMessage(connect, answers.Add);
		ops.ConnectCalls.ShouldBe(1);
		ops.PendingConnects.Single()(LinkConnectResult.Ok(new LinkAccount { Address = "P2KTest" }, "FakeWallet", "9.9.9", "localnet"));

		answers.Count.ShouldBe(1);
	}

	// A wallet operation that throws on a connect still gets the dApp an answer, -32603, also when
	// the dispatcher reached it from a later callback of the wallet, where no caller would catch it.
	// A callback that comes after the throw stores no session.
	[Fact]
	public void A_connect_whose_wallet_operation_throws_gets_an_internal_error()
	{
		foreach (var deferChains in new[] { false, true })
		{
			var ops = new FakeV5Ops { ThrowOnConnect = true, DeferChains = deferChains };
			var sessions = new InMemoryLinkSessionStore();
			var link = new WalletLinkV5(ops, sessions);
			var answers = new List<string>();

			link.HandleMessage("{\"plv\":5,\"id\":\"c9\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"testdapp\",\"url\":\"https://t.example\"}}}", answers.Add);
			if (deferChains)
			{
				ops.PendingChains.Single()();
			}

			answers.Count.ShouldBe(1);
			((int)JObject.Parse(answers[0])["error"]!["code"]!).ShouldBe(-32603);
			ops.PendingConnects.Single()(LinkConnectResult.Ok(new LinkAccount { Address = "P2KTest" }, "FakeWallet", "9.9.9", "localnet"));
			answers.Count.ShouldBe(1);
			sessions.List().ShouldBeEmpty();
		}
	}

	// Spec §18.2: the channel keeps the id of a connect after the answer too. A relay re-send of a
	// connect the user rejected prompts no second time; the same id on another channel is another
	// request.
	[Fact]
	public void A_copy_of_an_answered_connect_is_dropped_on_its_channel()
	{
		var ops = new FakeV5Ops { RejectConnect = true };
		var link = new WalletLinkV5(ops);
		var connect = "{\"plv\":5,\"id\":\"c8\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"testdapp\",\"url\":\"https://t.example\"}}}";
		var pairing = new LinkRequestContext { Transport = LinkTransportKind.Relay, Channel = "pairing a" };
		var answers = new List<string>();

		link.HandleMessage(connect, answers.Add, pairing);
		link.HandleMessage(connect, answers.Add, new LinkRequestContext { Transport = LinkTransportKind.Relay, Channel = "pairing a" });
		answers.Count.ShouldBe(1);
		((int)JObject.Parse(answers[0])["error"]!["code"]!).ShouldBe(4001);
		ops.ConnectCalls.ShouldBe(1);

		link.HandleMessage(connect, answers.Add, new LinkRequestContext { Transport = LinkTransportKind.Relay, Channel = "pairing b" });
		answers.Count.ShouldBe(2);
		ops.ConnectCalls.ShouldBe(2);
	}

	// Spec §4: an id holds at most 128 characters. A longer one is refused, and the answer does not
	// repeat it.
	[Fact]
	public void A_request_id_longer_than_128_characters_gets_invalid_request()
	{
		var link = new WalletLinkV5(new FakeV5Ops());
		var longest = new string('i', 128);

		var kept = Send(link, $"{{\"plv\":5,\"id\":\"{longest}\",\"method\":\"pha_getChains\"}}");
		((string)kept["id"]!).ShouldBe(longest);
		var refused = Send(link, $"{{\"plv\":5,\"id\":\"{longest}i\",\"method\":\"pha_getChains\"}}");
		((int)refused["error"]!["code"]!).ShouldBe(-32600);
		refused["id"]!.Type.ShouldBe(JTokenType.Null);
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
			$"{{\"plv\":5,\"id\":\"r1\",\"session\":\"{session}\",\"method\":\"pha_connect\",\"params\":{{\"dapp\":{{\"name\":\"testdapp\",\"url\":\"https://testdapp.example\"}}}}}}");

		ops.ConnectCalls.ShouldBe(1); // NO second prompt
		((string)resumed["result"]!["session"]!["id"]!).ShouldBe(session); // same session id
		((string)resumed["result"]!["account"]!["address"]!).ShouldBe("P2KTest");
		((string)resumed["result"]!["wallet"]!["name"]!).ShouldBe("FakeWallet");
	}

	[Fact]
	public void A_session_id_in_the_params_is_not_a_resume()
	{
		// Spec section 9.5: the session to resume rides in the envelope; the params carry none.
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);

		var response = Send(link,
			$"{{\"plv\":5,\"id\":\"r1\",\"method\":\"pha_connect\",\"params\":{{\"dapp\":{{\"name\":\"testdapp\",\"url\":\"https://t.example\"}},\"session\":\"{session}\"}}}}");

		ops.ConnectCalls.ShouldBe(2);
		((string)response["result"]!["session"]!["id"]!).ShouldNotBe(session);
	}

	[Fact]
	public void Resume_with_unknown_id_falls_back_to_the_consent_prompt()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);

		var response = Send(link,
			"{\"plv\":5,\"id\":\"r1\",\"session\":\"nope\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"testdapp\",\"url\":\"https://testdapp.example\"}}}");

		ops.ConnectCalls.ShouldBe(1); // prompted
		((string)response["result"]!["session"]!["id"]!).ShouldNotBe("nope"); // fresh session
	}

	// Spec §7: a loopback session made by a caller without an origin is held to the dApp name it was
	// approved for. Its resume with another dApp name gets 4100, and the session stays.
	[Fact]
	public void An_originless_session_resumes_only_for_its_dapp_name()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);

		var response = Send(link,
			$"{{\"plv\":5,\"id\":\"r1\",\"session\":\"{session}\",\"method\":\"pha_connect\",\"params\":{{\"dapp\":{{\"name\":\"OTHER\",\"url\":\"https://other.example\"}}}}}}");

		((int)response["error"]!["code"]!).ShouldBe(4100);
		ops.ConnectCalls.ShouldBe(1);
		link.IsSessionLive(session).ShouldBeTrue();
	}

	[Fact]
	public void Resume_while_another_account_is_selected_gets_5006_and_keeps_the_session()
	{
		// Spec §7: the account rule applies to the resume too; the session stays on both sides.
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);

		ops.AccountAddress = "P2KOther";
		var response = Send(link,
			$"{{\"plv\":5,\"id\":\"r1\",\"session\":\"{session}\",\"method\":\"pha_connect\",\"params\":{{\"dapp\":{{\"name\":\"testdapp\",\"url\":\"https://testdapp.example\"}}}}}}");

		((int)response["error"]!["code"]!).ShouldBe(5006);
		((string)response["error"]!["data"]!["address"]!).ShouldBe("P2KTest");
		ops.ConnectCalls.ShouldBe(1);
		link.IsSessionLive(session).ShouldBeTrue();
	}

	[Fact]
	public void A_request_while_another_account_is_selected_gets_5006_and_the_account_again_serves_it()
	{
		// Spec §7: every request in the session except pha_disconnect gets 5006 with the session's
		// account; when the user selects that account again, the session hears pha_accountsChanged
		// and its requests are served.
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);
		var events = new List<JObject>();
		link.SessionEvent += (_, envelope) => events.Add(JObject.Parse(envelope));

		ops.AccountAddress = "P2KOther";
		link.SelectedAccountChanged();
		var refused = Send(link, $"{{\"plv\":5,\"id\":\"a1\",\"session\":\"{session}\",\"method\":\"pha_getAccounts\"}}");
		ops.AccountAddress = "P2KTest";
		link.SelectedAccountChanged();
		var served = Send(link, $"{{\"plv\":5,\"id\":\"a2\",\"session\":\"{session}\",\"method\":\"pha_getAccounts\"}}");

		((int)refused["error"]!["code"]!).ShouldBe(5006);
		((string)refused["error"]!["data"]!["address"]!).ShouldBe("P2KTest");
		var notice = events.Single();
		((string)notice["event"]!).ShouldBe("pha_accountsChanged");
		((string)notice["session"]!).ShouldBe(session);
		((string)notice["data"]!["accounts"]![0]!["address"]!).ShouldBe("P2KTest");
		((string)served["result"]!["accounts"]![0]!["address"]!).ShouldBe("P2KTest");
	}

	[Fact]
	public void A_disconnect_while_another_account_is_selected_ends_the_session()
	{
		var ops = new FakeV5Ops();
		var link = new WalletLinkV5(ops);
		var session = Connect(link, out _);
		ops.AccountAddress = "P2KOther";

		var response = Send(link, $"{{\"plv\":5,\"id\":\"d1\",\"session\":\"{session}\",\"method\":\"pha_disconnect\"}}");

		((bool)response["result"]!["ok"]!).ShouldBeTrue();
		link.IsSessionLive(session).ShouldBeFalse();
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
			$"{{\"plv\":5,\"id\":\"r1\",\"session\":\"{session}\",\"method\":\"pha_connect\",\"params\":{{\"dapp\":{{\"name\":\"testdapp\",\"url\":\"https://testdapp.example\"}}}}}}");

		ops.ConnectCalls.ShouldBe(1); // still no second prompt
		((string)resumed["result"]!["session"]!["id"]!).ShouldBe(session);

		// And the resumed session is live for authenticated calls on the new dispatcher.
		var chains = Send(second, $"{{\"plv\":5,\"id\":\"r2\",\"session\":\"{session}\",\"method\":\"pha_getChains\"}}");
		((string)chains["result"]!["nexus"]!).ShouldBe("localnet");
	}
}

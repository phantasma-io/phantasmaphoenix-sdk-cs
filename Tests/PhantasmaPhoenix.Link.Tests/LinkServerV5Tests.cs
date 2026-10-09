using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Link.Tests;

/// <summary>
/// The v5 endpoint of the loopback server (spec §6.2) over a real socket on a free port, with the
/// client a browser page uses.
/// </summary>
public class LinkServerV5Tests
{
	/// <summary>The legacy wallet the server needs; the v5 tests never reach it.</summary>
	private sealed class UnusedLegacyWallet : WalletLink
	{
		protected override WalletStatus Status => WalletStatus.Closed;
		public override string Nexus => "localnet";
		public override string Name => "unused";
		protected override void Authorize(string dapp, string token, int version, Action<bool, string> callback) => throw new NotSupportedException();
		protected override void GetAccount(string platform, int version, Action<Account, string> callback) => throw new NotSupportedException();
		protected override void GetPeer(Action<string> callback) => throw new NotSupportedException();
		protected override void GetNexus(Action<string> callback) => throw new NotSupportedException();
		protected override void GetN3Address(Action<string> callback) => throw new NotSupportedException();
		protected override void GetWalletVersion(Action<string> callback) => throw new NotSupportedException();
		protected override void InvokeScript(string chain, byte[] script, int id, Action<string[], string> callback) => throw new NotSupportedException();
		protected override void SignData(string platform, SignatureKind kind, byte[] data, int id, Action<string, string, string> callback) => throw new NotSupportedException();
		protected override void SignTransactionSignature(PhantasmaPhoenix.Protocol.Transaction transaction, string platform, SignatureKind kind, Action<PhantasmaPhoenix.Cryptography.Signature, string> callback) => throw new NotSupportedException();
		protected override void FetchAndMultiSignature(string subject, string platform, SignatureKind kind, int id, Action<bool, string> callback) => throw new NotSupportedException();
		protected override void SignTransaction(string platform, SignatureKind kind, string chain, byte[] script, byte[] payload, int id, ProofOfWork pow, Action<Hash, string> callback) => throw new NotSupportedException();
		protected override void SignCarbonTransactionAndBroadcast(byte[] transaction, Action<Hash, string> callback) => throw new NotSupportedException();
		protected override void WriteArchive(Hash hash, int blockIndex, byte[] data, Action<bool, string> callback) => throw new NotSupportedException();
	}

	/// <summary>A wallet that approves every connect with one account; the tests sign nothing.</summary>
	private sealed class ApprovingWallet : IWalletLinkV5Ops
	{
		private static readonly LinkAccount Account = new LinkAccount { Address = "P2KTest", Name = "tester" };

		/// <summary>The caller of the last connect.</summary>
		public LinkCaller? LastCaller;

		public WalletStatus Status => WalletStatus.Ready;
		public string? SelectedAddress => Account.Address;
		public void Connect(LinkCaller caller, Action<LinkConnectResult> done)
		{
			LastCaller = caller;
			done(LinkConnectResult.Ok(Account, "TestWallet", "1.0.0", "localnet"));
		}
		public void GetAccount(Action<LinkAccountResult> done) => done(LinkAccountResult.Ok(Account));
		public void GetChains(Action<LinkChains> done) => done(new LinkChains { Nexus = "localnet" });
		public void GetWalletInfo(Action<LinkWalletInfo> done) =>
			done(new LinkWalletInfo { Name = "TestWallet", Version = "1.0.0", Rpc = "http://localhost:5172/rpc" });
		public void SendTransaction(LinkCaller caller, byte[] serializedTx, LinkTxFormat format, SignatureKind kind, Action<LinkSendResult> done) =>
			done(LinkSendResult.Fail(LinkFailure.UserRejected, "rejected"));
		public void SignMessage(LinkCaller caller, byte[] message, string? display, Action<LinkSignMessageResult> done) =>
			done(LinkSignMessageResult.Fail(LinkFailure.UserRejected, "rejected"));
		public void SignTransaction(LinkCaller caller, byte[] serializedTx, LinkTxFormat format, SignatureKind kind, Action<LinkSignTransactionResult> done) =>
			done(LinkSignTransactionResult.Fail(LinkFailure.UserRejected, "rejected"));
		public void ConfirmPairing(LinkPairingParams pairing, Action<LinkPairingDecision> done) => done(LinkPairingDecision.Decline());
		public void InvokeScript(string chain, byte[] script, Action<LinkInvokeResult> done) =>
			done(LinkInvokeResult.Ok(Array.Empty<byte[]>()));
	}

	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

	private static (LinkServer server, WalletLinkV5 link) StartServer() => StartServer(new ApprovingWallet());

	private static (LinkServer server, WalletLinkV5 link) StartServer(ApprovingWallet wallet)
	{
		var link = new WalletLinkV5(wallet);
		var server = new LinkServer(new UnusedLegacyWallet(), link, port: 0);
		// The requests run on the connection thread; a wallet posts them to its UI thread.
		server.OnUI = action => action();
		server.Start();
		return (server, link);
	}

	/// <summary>Opens a connection as a page of <paramref name="origin"/> does, or as a client
	/// without an origin.</summary>
	private static async Task<ClientWebSocket> Open(LinkServer server, string? origin = null)
	{
		var client = new ClientWebSocket();
		if (origin != null)
		{
			client.Options.SetRequestHeader("Origin", origin);
		}
		using var timeout = new CancellationTokenSource(Wait);
		await client.ConnectAsync(new Uri($"ws://127.0.0.1:{server.Port}/phantasma/v5"), timeout.Token);
		return client;
	}

	private static async Task Send(ClientWebSocket client, string text, bool endOfMessage = true)
	{
		using var timeout = new CancellationTokenSource(Wait);
		await client.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, endOfMessage, timeout.Token);
	}

	private static async Task<string> ConnectSession(ClientWebSocket client)
	{
		await Send(client, ConnectRequest);
		return (string)(await Receive(client, Wait))!["result"]!["session"]!["id"]!;
	}

	/// <summary>Returns the next message, or null when none comes within <paramref name="wait"/>.</summary>
	private static async Task<JObject?> Receive(ClientWebSocket client, TimeSpan wait)
	{
		using var timeout = new CancellationTokenSource(wait);
		using var message = new MemoryStream();
		var buffer = new byte[64 * 1024];
		try
		{
			while (true)
			{
				var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
				message.Write(buffer, 0, result.Count);
				if (result.EndOfMessage)
				{
					return JObject.Parse(Encoding.UTF8.GetString(message.ToArray()));
				}
			}
		}
		catch (OperationCanceledException)
		{
			return null;
		}
	}

	private const string ConnectRequest =
		"{\"plv\":5,\"id\":\"c1\",\"method\":\"pha_connect\",\"params\":{\"dapp\":{\"name\":\"page\",\"url\":\"https://page.example\"}}}";

	// Spec §18.2: a connect of a caller without an origin is remembered with its connection. Two
	// pages without an origin that pick the same id each get their own answer.
	[Fact]
	public async Task Connects_of_two_connections_without_an_origin_are_two_requests()
	{
		var (server, _) = StartServer();
		try
		{
			using var first = await Open(server);
			using var second = await Open(server);

			var one = await ConnectSession(first);
			var two = await ConnectSession(second);

			one.ShouldNotBe(two);
		}
		finally
		{
			server.Stop();
		}
	}

	// A stopped server serves no page: the connections made before close too, so no request of
	// theirs reaches a prompt of the wallet.
	[Fact]
	public async Task Stopping_the_server_closes_its_connections()
	{
		var (server, _) = StartServer();
		using var client = await Open(server, "https://page.example");
		await ConnectSession(client);

		server.Stop();

		using var timeout = new CancellationTokenSource(Wait);
		var closed = false;
		try
		{
			var result = await client.ReceiveAsync(new ArraySegment<byte>(new byte[1024]), timeout.Token);
			closed = result.MessageType == WebSocketMessageType.Close;
		}
		catch (WebSocketException)
		{
			closed = true;
		}
		closed.ShouldBeTrue();
	}

	[Fact]
	public async Task The_events_of_a_session_reach_the_connection_that_holds_it_only()
	{
		var (server, link) = StartServer();
		try
		{
			using var holder = await Open(server);
			using var other = await Open(server);
			await Send(holder, ConnectRequest);
			var session = (string)(await Receive(holder, Wait))!["result"]!["session"]!["id"]!;

			link.ChainChanged();
			var chainChanged = await Receive(holder, Wait);
			link.EndSession(session, LinkSessionEndReason.Revoked);
			var deleted = await Receive(holder, Wait);

			((string)chainChanged!["event"]!).ShouldBe("pha_chainChanged");
			((string)chainChanged["session"]!).ShouldBe(session);
			((string)chainChanged["data"]!["chain"]!).ShouldBe("phantasma:localnet");
			((string)deleted!["event"]!).ShouldBe("pha_sessionDeleted");
			((string)deleted["data"]!["session"]!).ShouldBe(session);
			(await Receive(other, TimeSpan.FromMilliseconds(300))).ShouldBeNull();
		}
		finally
		{
			server.Stop();
		}
	}

	[Fact]
	public async Task A_request_with_the_largest_transaction_fits_one_message()
	{
		// Spec §11: loopback takes a 32 MiB transaction, which is about 44.7 million characters in
		// base64; a browser sends the whole message as one frame.
		var (server, _) = StartServer();
		try
		{
			using var client = await Open(server);
			var session = await ConnectSession(client);
			var script = Convert.ToBase64String(new byte[WalletLinkV5.MaxTxBytes]);

			await Send(client, $"{{\"plv\":5,\"id\":\"big\",\"session\":\"{session}\",\"method\":\"pha_invokeScript\",\"params\":{{\"chain\":\"main\",\"script\":\"{script}\"}}}}");
			var answer = await Receive(client, TimeSpan.FromSeconds(60));

			((string)answer!["id"]!).ShouldBe("big");
			answer["result"].ShouldNotBeNull();
		}
		finally
		{
			server.Stop();
		}
	}

	[Fact]
	public async Task A_message_in_several_frames_is_one_request()
	{
		var (server, _) = StartServer();
		try
		{
			using var client = await Open(server);

			await Send(client, ConnectRequest.Substring(0, 20), endOfMessage: false);
			await Send(client, ConnectRequest.Substring(20, 30), endOfMessage: false);
			await Send(client, ConnectRequest.Substring(50), endOfMessage: true);
			var answer = await Receive(client, Wait);

			((string)answer!["id"]!).ShouldBe("c1");
			answer["result"]!["session"]!["id"].ShouldNotBeNull();
			(await Receive(client, TimeSpan.FromMilliseconds(300))).ShouldBeNull();
		}
		finally
		{
			server.Stop();
		}
	}

	[Fact]
	public async Task A_message_above_the_limit_gets_5001_with_its_id_and_the_connection_stays()
	{
		// Spec §10, §11: a clear 5001 instead of a silent failure. The message comes in frames,
		// each below the frame limit; together they pass the message limit.
		var (server, _) = StartServer();
		try
		{
			using var client = await Open(server);
			await Send(client, "{\"plv\":5,\"id\":\"huge\",\"method\":\"pha_invokeScript\",\"params\":{\"chain\":\"main\",\"script\":\"", endOfMessage: false);
			var part = new string('A', 10 * 1024 * 1024);
			for (var i = 0; i < 5; i++)
			{
				await Send(client, part, endOfMessage: false);
			}
			await Send(client, "\"}}", endOfMessage: true);

			var answer = await Receive(client, TimeSpan.FromSeconds(60));
			((string)answer!["id"]!).ShouldBe("huge");
			((int)answer["error"]!["code"]!).ShouldBe(5001);
			((int)answer["error"]!["data"]!["maxPayloadBytes"]!).ShouldBe(WalletLinkV5.MaxTxBytes);

			await Send(client, ConnectRequest);
			((string)(await Receive(client, Wait))!["id"]!).ShouldBe("c1");
		}
		finally
		{
			server.Stop();
		}
	}

	// Spec §4: an answer never repeats an id longer than 128 characters, also the 5001 of a message
	// above the limit, which then gets no answer; the connection stays.
	[Fact]
	public async Task A_message_above_the_limit_with_a_long_id_gets_no_answer()
	{
		var (server, _) = StartServer();
		try
		{
			using var client = await Open(server);
			var longId = new string('i', 129);
			await Send(client, "{\"plv\":5,\"id\":\"" + longId + "\",\"method\":\"pha_invokeScript\",\"params\":{\"chain\":\"main\",\"script\":\"", endOfMessage: false);
			var part = new string('A', 10 * 1024 * 1024);
			for (var i = 0; i < 5; i++)
			{
				await Send(client, part, endOfMessage: false);
			}
			await Send(client, "\"}}", endOfMessage: true);

			await Send(client, ConnectRequest);
			((string)(await Receive(client, TimeSpan.FromSeconds(60)))!["id"]!).ShouldBe("c1");
		}
		finally
		{
			server.Stop();
		}
	}

	[Fact]
	public async Task A_frame_above_the_limit_closes_with_1009_and_no_exception_text()
	{
		// RFC 6455: a close frame holds 125 bytes at most. Exception text would also leak internals.
		var (server, _) = StartServer();
		try
		{
			using var tcp = new TcpClient();
			await tcp.ConnectAsync(IPAddress.Loopback, server.Port);
			var stream = tcp.GetStream();
			stream.ReadTimeout = (int)Wait.TotalMilliseconds;
			var handshake = $"GET /phantasma/v5 HTTP/1.1\r\nHost: 127.0.0.1:{server.Port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
				+ $"Sec-WebSocket-Key: {Convert.ToBase64String(new byte[16])}\r\nSec-WebSocket-Version: 13\r\n\r\n";
			var request = Encoding.ASCII.GetBytes(handshake);
			stream.Write(request, 0, request.Length);
			var head = new StringBuilder();
			while (!head.ToString().EndsWith("\r\n\r\n"))
			{
				head.Append((char)stream.ReadByte());
			}
			head.ToString().ShouldStartWith("HTTP/1.1 101");

			// A masked text frame that announces 100 000 000 bytes; the server refuses it at once.
			var frameHead = new byte[] { 0x81, 0x80 | 127, 0, 0, 0, 0, 0x05, 0xF5, 0xE1, 0x00 };
			stream.Write(frameHead, 0, frameHead.Length);

			var close = new List<byte>();
			int next;
			while ((next = stream.ReadByte()) >= 0)
			{
				close.Add((byte)next);
			}
			close[0].ShouldBe((byte)0x88);
			var length = close[1];
			length.ShouldBeLessThanOrEqualTo((byte)125);
			((close[2] << 8) | close[3]).ShouldBe(1009);
			var reason = Encoding.UTF8.GetString(close.Skip(4).Take(length - 2).ToArray());
			reason.ShouldNotContain("Exception");
		}
		finally
		{
			server.Stop();
		}
	}

	[Fact]
	public async Task The_page_origin_reaches_the_wallet_and_binds_the_session()
	{
		// Spec §6.2: the wallet binds the session to the requesting browser origin, and a caller
		// without one is unverified.
		var wallet = new ApprovingWallet();
		var (server, _) = StartServer(wallet);
		try
		{
			using var page = await Open(server, "http://localhost:3000");
			var session = await ConnectSession(page);
			wallet.LastCaller!.Origin.ShouldBe("http://localhost:3000");

			using var other = await Open(server, "http://other.example");
			await Send(other, $"{{\"plv\":5,\"id\":\"g1\",\"session\":\"{session}\",\"method\":\"pha_getChains\"}}");
			((int)(await Receive(other, Wait))!["error"]!["code"]!).ShouldBe(4100);

			using var app = await Open(server);
			await ConnectSession(app);
			wallet.LastCaller!.Origin.ShouldBeNull();
		}
		finally
		{
			server.Stop();
		}
	}
}

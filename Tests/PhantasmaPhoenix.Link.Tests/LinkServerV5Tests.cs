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

		public WalletStatus Status => WalletStatus.Ready;
		public string? SelectedAddress => Account.Address;
		public void Connect(LinkCaller caller, Action<LinkConnectResult> done) =>
			done(LinkConnectResult.Ok(Account, "TestWallet", "1.0.0", "localnet"));
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

	private static (LinkServer server, WalletLinkV5 link) StartServer()
	{
		var link = new WalletLinkV5(new ApprovingWallet());
		var server = new LinkServer(new UnusedLegacyWallet(), link, port: 0);
		// The requests run on the connection thread; a wallet posts them to its UI thread.
		server.OnUI = action => action();
		server.Start();
		return (server, link);
	}

	private static async Task<ClientWebSocket> Open(LinkServer server)
	{
		var client = new ClientWebSocket();
		using var timeout = new CancellationTokenSource(Wait);
		await client.ConnectAsync(new Uri($"ws://127.0.0.1:{server.Port}/phantasma/v5"), timeout.Token);
		return client;
	}

	private static async Task Send(ClientWebSocket client, string text)
	{
		using var timeout = new CancellationTokenSource(Wait);
		await client.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, timeout.Token);
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
}

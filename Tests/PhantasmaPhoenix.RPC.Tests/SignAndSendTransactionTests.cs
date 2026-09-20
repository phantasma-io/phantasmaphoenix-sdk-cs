using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.RPC.Tests;

// The script path builds, signs and broadcasts inside one call, so what it stamped is read back
// from the transaction the node was given. The stub answers with the hash of what it received,
// which is what the node does and what the call checks before it returns.
public class SignAndSendTransactionTests
{
	private sealed class RecordingNode : HttpMessageHandler
	{
		public List<string> Sent { get; } = new();

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var body = JObject.Parse(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
			var id = body["id"]!.DeepClone();
			var method = body["method"]!.Value<string>();
			if (method != "sendRawTransaction")
			{
				return Respond(HttpStatusCode.BadRequest, new JObject
				{
					["jsonrpc"] = "2.0",
					["id"] = id,
					["error"] = new JObject { ["code"] = -32603, ["message"] = $"unexpected method {method}" }
				});
			}

			var encoded = ((JArray)body["params"]!)[0]!.Value<string>()!;
			Sent.Add(encoded);
			var hash = Transaction.Unserialize(Base16.Decode(encoded)!)!.Hash.ToString();
			return Respond(HttpStatusCode.OK, new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = hash });
		}

		private static HttpResponseMessage Respond(HttpStatusCode status, JObject envelope) => new(status)
		{
			Content = new StringContent(envelope.ToString(Formatting.None), Encoding.UTF8, "application/json")
		};
	}

	private static readonly PhantasmaKeys Signer = PhantasmaKeys.FromWIF("KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d");
	private static readonly byte[] Script = new byte[] { 0x0d, 0x00, 0x0b };

	private static (PhantasmaAPI api, RecordingNode node) Api()
	{
		var node = new RecordingNode();
		return (new PhantasmaAPI("http://localhost/rpc", new RpcClient(new HttpClient(node))), node);
	}

	private static Transaction Broadcast(RecordingNode node) =>
		Transaction.Unserialize(Base16.Decode(node.Sent.Single())!)!;

	// A chain refuses an expiry at or beyond its own window, and that window can be as short as the
	// node default. Both transaction paths are admitted by that same check, so they stamp the same
	// lifetime. This one counts in seconds while TxLimits counts in milliseconds.
	[Fact]
	public async Task Stamps_the_Carbon_default_lifetime_when_the_caller_names_no_expiration()
	{
		var (api, node) = Api();
		using (api)
		{
			var lifetimeSeconds = (uint)(TxLimits.DefaultExpiryMs / 1000);
			var before = Timestamp.Now.Value;
			await api.SignAndSendTransactionAsync(Signer, "simnet", Script, "main", string.Empty);
			var after = Timestamp.Now.Value;

			Broadcast(node).Expiration.Value.ShouldBeInRange(before + lifetimeSeconds, after + lifetimeSeconds);
		}
	}

	[Fact]
	public async Task Keeps_the_expiration_the_caller_passes()
	{
		var (api, node) = Api();
		using (api)
		{
			var expiration = new Timestamp(1_900_000_000);
			await api.SignAndSendTransactionAsync(Signer, "simnet", Script, "main", string.Empty, null, expiration);

			Broadcast(node).Expiration.Value.ShouldBe(expiration.Value);
		}
	}
}

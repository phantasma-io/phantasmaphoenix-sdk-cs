using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.RPC.Tests;

public class RpcClientIdTests
{
	[Fact]
	public async Task SendRpcAsync_Accepts_Response_With_Matching_Id()
	{
		// The client generates an id per request and must accept only the response that echoes it.
		using var client = new RpcClient(new HttpClient(new StubHandler(requestJson =>
		{
			var request = JObject.Parse(requestJson);
			return $$"""{"jsonrpc":"2.0","id":{{request["id"]!.ToString(Newtonsoft.Json.Formatting.None)}},"result":true}""";
		})));

		var result = await client.SendRpcAsync<bool>("http://localhost/rpc", "getVersion");

		result.ShouldBeTrue();
	}

	[Fact]
	public async Task SendRpcAsync_Generates_Unique_Request_Ids_And_Requires_Echo()
	{
		var requestIds = new List<string>();
		using var client = new RpcClient(new HttpClient(new StubHandler(requestJson =>
		{
			var request = JObject.Parse(requestJson);
			var requestId = request["id"]!.ToString();
			requestIds.Add(requestId);
			return $$"""{"jsonrpc":"2.0","id":{{request["id"]!.ToString(Newtonsoft.Json.Formatting.None)}},"result":true}""";
		})));

		(await client.SendRpcAsync<bool>("http://localhost/rpc", "first")).ShouldBeTrue();
		(await client.SendRpcAsync<bool>("http://localhost/rpc", "second")).ShouldBeTrue();

		requestIds.Count.ShouldBe(2);
		requestIds[0].ShouldNotBe(requestIds[1]);
		Guid.TryParse(requestIds[0], out _).ShouldBeTrue();
		Guid.TryParse(requestIds[1], out _).ShouldBeTrue();
	}

	[Fact]
	public async Task SendRpcAsync_Rejects_Stale_Response_Id_After_Request_Id_Changes()
	{
		// A stale response echo from the previous request must be rejected
		// before the second call can consume its result body.
		var requestIds = new List<string>();
		using var client = new RpcClient(new HttpClient(new StubHandler(requestJson =>
		{
			var request = JObject.Parse(requestJson);
			var requestId = request["id"]!.ToString();
			requestIds.Add(requestId);
			var responseId = requestIds.Count == 2 ? requestIds[0] : requestId;
			return $$"""{"jsonrpc":"2.0","id":"{{responseId}}","result":true}""";
		})));

		(await client.SendRpcAsync<bool>("http://localhost/rpc", "first")).ShouldBeTrue();
		var error = await Should.ThrowAsync<Exception>(() =>
			client.SendRpcAsync<bool>("http://localhost/rpc", "second"));

		requestIds.Count.ShouldBe(2);
		requestIds[0].ShouldNotBe(requestIds[1]);
		error.Message.ShouldContain("Response id mismatch");
		error.Message.ShouldContain(requestIds[0]);
		error.Message.ShouldContain(requestIds[1]);
	}

	[Theory]
	[InlineData(@"{""jsonrpc"":""2.0"",""result"":true}", "Missing response id")]
	[InlineData(@"{""jsonrpc"":""2.0"",""id"":null,""result"":true}", "Missing response id")]
	[InlineData(@"{""jsonrpc"":""2.0"",""id"":""not-the-request"",""result"":true}", "Response id mismatch")]
	[InlineData(@"{""jsonrpc"":""2.0"",""id"":0,""result"":true}", "Response id mismatch")]
	public async Task SendRpcAsync_Rejects_Uncorrelated_Response_Id(string responseJson, string expectedMessage)
	{
		// Missing, null, or mismatched ids cannot be correlated with the generated request id.
		using var client = new RpcClient(new HttpClient(new StubHandler(_ => responseJson)));

		var error = await Should.ThrowAsync<Exception>(() =>
			client.SendRpcAsync<bool>("http://localhost/rpc", "getVersion"));

		error.Message.ShouldContain(expectedMessage);
	}

	[Fact]
	public async Task SendRpcAsync_Rejects_Id_Mismatch_Before_Rpc_Error()
	{
		using var client = new RpcClient(new HttpClient(new StubHandler(_ =>
			@"{""jsonrpc"":""2.0"",""id"":""not-the-request"",""error"":{""code"":-32603,""message"":""Execution failed""}}")));

		var error = await Should.ThrowAsync<Exception>(() =>
			client.SendRpcAsync<bool>("http://localhost/rpc", "getVersion"));

		error.Message.ShouldContain("Response id mismatch");
		error.Message.ShouldNotContain("Execution failed");
	}

	[Fact]
	public async Task SendRpcAsync_Rejects_Response_Id_With_Wrong_Json_Type()
	{
		using var client = new RpcClient(new HttpClient(new StubHandler(_ =>
			@"{""jsonrpc"":""2.0"",""id"":{""bad"":""id""},""result"":true}")));

		var error = await Should.ThrowAsync<Exception>(() =>
			client.SendRpcAsync<bool>("http://localhost/rpc", "getVersion"));

		error.Message.ShouldContain("JSON-RPC id must be a string, integer, or null");
	}

	private sealed class StubHandler : HttpMessageHandler
	{
		private readonly Func<string, string> _responseFactory;

		public StubHandler(Func<string, string> responseFactory)
		{
			_responseFactory = responseFactory;
		}

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var requestJson = request.Content == null
				? string.Empty
				: await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(_responseFactory(requestJson), Encoding.UTF8, "application/json")
			};
		}
	}
}

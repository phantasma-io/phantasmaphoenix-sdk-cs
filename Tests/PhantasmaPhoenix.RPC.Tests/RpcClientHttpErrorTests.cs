using System.Net;
using System.Text;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.RPC.Tests;

// The node answers an application failure with an HTTP error status AND a JSON-RPC error body. The
// body is the answer. "Token symbol not found" is what a caller can act on, so it must reach the
// caller and must not hide behind the status line. A status with no envelope keeps the
// HTTP-shaped message, so infrastructure rejections stay recognisable.
public class RpcClientHttpErrorTests
{
	private sealed class CannedHandler : HttpMessageHandler
	{
		private readonly HttpStatusCode _status;
		private readonly string _body;
		public CannedHandler(HttpStatusCode status, string body) { _status = status; _body = body; }
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") });
	}

	[Fact]
	public async Task Surfaces_the_JSON_RPC_error_carried_by_an_HTTP_error_response()
	{
		using var handler = new CannedHandler(HttpStatusCode.BadRequest,
			"""{"jsonrpc":"2.0","id":"1","error":{"code":-32603,"message":"Token symbol not found"}}""");
		using var rpc = new RpcClient(new HttpClient(handler));

		var error = await Should.ThrowAsync<Exception>(() => rpc.SendRpcAsync<object>("http://localhost/rpc", "getToken", "NOPE"));
		error.Message.ShouldContain("Token symbol not found");
		error.Message.ShouldContain("-32603");
		error.Message.ShouldNotContain("BadRequest");
	}

	[Fact]
	public async Task Keeps_the_HTTP_status_when_the_error_body_is_not_a_JSON_RPC_envelope()
	{
		using var handler = new CannedHandler(HttpStatusCode.TooManyRequests, "rate limit exceeded");
		using var rpc = new RpcClient(new HttpClient(handler));

		var error = await Should.ThrowAsync<Exception>(() => rpc.SendRpcAsync<object>("http://localhost/rpc", "getToken", "KCAL"));
		error.Message.ShouldContain("429");
		error.Message.ShouldContain("rate limit exceeded");
	}
}

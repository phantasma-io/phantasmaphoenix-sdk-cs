using Newtonsoft.Json;

namespace PhantasmaPhoenix.RPC.Types;

public class RpcResponse
{
	public string Jsonrpc { get; set; } = "2.0";
	public int Id { get; set; } = 1;
	[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
	public object? Result { get; set; }
	[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
	public RpcError? Error { get; set; }

	public RpcResponse(object? result, RpcError? error)
	{
		Result = result;
		Error = error;
	}
}

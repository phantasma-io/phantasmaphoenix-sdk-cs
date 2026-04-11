using Newtonsoft.Json;

namespace PhantasmaPhoenix.RPC.Types;

public class RpcResponse
{
	[JsonProperty("jsonrpc", Required = Required.Always)]
	public string jsonrpc { get; set; } = "2.0";
	[JsonProperty("id", NullValueHandling = NullValueHandling.Include)]
	public RpcId? id { get; set; }
	[JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
	public object? Result { get; set; }
	[JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
	public RpcError? Error { get; set; }

	public RpcResponse(RpcId? id, object? result, RpcError? error)
	{
		this.id = id;
		Result = result;
		Error = error;
	}

	public RpcResponse()
	{
	}
}

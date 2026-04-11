using Newtonsoft.Json;

namespace PhantasmaPhoenix.RPC.Types;

public struct RpcRequest
{
	[JsonProperty("jsonrpc", Required = Required.Always)]
	public string jsonrpc { get; set; }

	[JsonProperty("method", Required = Required.Always)]
	public string method { get; set; }

	[JsonProperty("id", NullValueHandling = NullValueHandling.Include)]
	public RpcId? id { get; set; }

	[JsonProperty("params", Required = Required.Always)]
	public object[] @params { get; set; }

	public override string ToString()
	{
		return $"RPC request '{method}' with {@params.Length} params";
	}
}

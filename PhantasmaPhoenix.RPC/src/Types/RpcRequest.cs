using Newtonsoft.Json;

namespace PhantasmaPhoenix.RPC.Types;

public struct RpcRequest
{
	[JsonProperty(Required = Required.Always)]
	public string jsonrpc { get; set; }

	[JsonProperty(Required = Required.Always)]
	public string method { get; set; }

	[JsonProperty(Required = Required.Always)]
	public string id { get; set; }

	[JsonProperty(Required = Required.Always)]
	public object[] @params { get; set; }

	public override string ToString()
	{
		return $"RPC request '{method}' with {@params.Length} params";
	}
}

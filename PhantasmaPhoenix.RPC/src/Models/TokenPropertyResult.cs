using Newtonsoft.Json;

namespace PhantasmaPhoenix.RPC.Models;

[JsonConverter(typeof(TokenPropertyResultJsonConverter))]
public class TokenPropertyResult
{
	public string Key { get; set; } = string.Empty;
	public string Value { get; set; } = string.Empty;

	public TokenPropertyResult() { }
}

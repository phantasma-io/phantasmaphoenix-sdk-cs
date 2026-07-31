using Newtonsoft.Json;

using PhantasmaPhoenix.Protocol;

namespace PhantasmaPhoenix.RPC.Models;

[JsonConverter(typeof(TokenPropertyResultJsonConverter))]
public class TokenPropertyResult
{
	public string Key { get; set; } = string.Empty;
	/// <summary>
	/// The decoded value. Scalars carry their content in <see cref="VmValue.Text"/>; VM structs and
	/// arrays keep their shape instead of being packed into a JSON string, which is what this field
	/// used to hold.
	/// </summary>
	public VmValue? Value { get; set; }

	public TokenPropertyResult() { }
}

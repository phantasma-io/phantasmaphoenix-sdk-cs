using Newtonsoft.Json;

namespace PhantasmaPhoenix.RPC.Models;

[JsonConverter(typeof(TransactionSignatureResultJsonConverter))]
public class TransactionSignatureResult
{
	public string Kind { get; set; } = string.Empty;
	public string Data { get; set; } = string.Empty;

	public TransactionSignatureResult() { }
}

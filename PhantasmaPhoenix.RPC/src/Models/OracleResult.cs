using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class OracleResult
{
	[ApiDescription("URL that was read by the oracle")]
	public string Url { get; set; }

	[ApiDescription("Byte array content read by the oracle, encoded as hex string")]
	public string Content { get; set; }

	public OracleResult() { }
}

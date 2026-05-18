using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class ScriptResult
{
	[ApiDescription("List of events that triggered in the transaction")]
	public EventResult[] Events { get; set; }

	public string? Result { get; set; }

	public string? Error { get; set; }

	[ApiDescription("Results of the transaction, if any. Serialized, in hexadecimal format")]
	public string[] Results { get; set; }

	[ApiDescription("List of oracle reads that were triggered in the transaction")]
	public OracleResult[] Oracles { get; set; }

	public string? State { get; set; }

	public string? Gas { get; set; }

	public ScriptResult() { }
}

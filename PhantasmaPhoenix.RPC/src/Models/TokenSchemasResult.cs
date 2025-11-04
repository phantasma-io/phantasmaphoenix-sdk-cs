using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.RPC.Models;

public class TokenSchemasResult
{
	public VmStructSchemaResult SeriesMetadata { get; set; }
	public VmStructSchemaResult Rom { get; set; }
	public VmStructSchemaResult Ram { get; set; }

	public TokenSchemasResult() { }
}

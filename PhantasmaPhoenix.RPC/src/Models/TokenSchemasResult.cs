using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.RPC.Models;

public class TokenSchemasResult
{
	public VmStructSchemaResult SeriesMetadata { get; set; } = new();
	public VmStructSchemaResult Rom { get; set; } = new();
	public VmStructSchemaResult Ram { get; set; } = new();

	public TokenSchemasResult() { }
}

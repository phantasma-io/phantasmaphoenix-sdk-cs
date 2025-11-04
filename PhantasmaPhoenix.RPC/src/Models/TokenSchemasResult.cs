using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.RPC.Models;

public class TokenSchemasResult
{
	public string Name { get; set; }
	public VmType Type { get; set; }

	public VmStructSchemaResult seriesMetadata { get; set; }
	public VmStructSchemaResult rom { get; set; }
	public VmStructSchemaResult ram { get; set; }

	public TokenSchemasResult() { }
}

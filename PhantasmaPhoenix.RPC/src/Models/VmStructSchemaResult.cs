namespace PhantasmaPhoenix.RPC.Models;

public class VmStructSchemaResult
{
	public VmNamedVariableSchemaResult[] Fields { get; set; } = Array.Empty<VmNamedVariableSchemaResult>();
	public uint Flags { get; set; }

	public VmStructSchemaResult() { }
}

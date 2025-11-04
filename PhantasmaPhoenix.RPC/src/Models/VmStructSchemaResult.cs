namespace PhantasmaPhoenix.RPC.Models;

public class VmStructSchemaResult
{
	public VmNamedVariableSchemaResult[] Fields { get; set; }
	public uint Flags { get; set; }

	public VmStructSchemaResult() { }
}

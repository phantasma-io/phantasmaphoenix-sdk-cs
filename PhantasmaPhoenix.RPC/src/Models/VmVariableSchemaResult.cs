using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.RPC.Models;

public class VmVariableSchemaResult
{
	public VmType Type { get; set; }
	public VmStructSchemaResult Schema { get; set; }


	public VmVariableSchemaResult() { }
}

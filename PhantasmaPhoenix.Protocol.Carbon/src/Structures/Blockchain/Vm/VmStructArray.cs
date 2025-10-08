namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

public class VmStructArray
{
	public VmStructSchema schema = new VmStructSchema { fields = Array.Empty<VmNamedVariableSchema>(), flags = VmStructSchema.Flags.None };
	public VmDynamicStruct[] structs = Array.Empty<VmDynamicStruct>();
}

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

public struct VmVariableSchema : ICarbonBlob
{
	public VmType type;
	public VmStructSchema structure;

	public void Write(BinaryWriter w)
	{
		w.Write1(type);
		if (type == VmType.Struct || type == (VmType.Struct | VmType.Array))
			structure.Write(w);
	}
	public void Read(BinaryReader r)
	{
		r.Read1(out type);
		if (type == VmType.Struct || type == (VmType.Struct | VmType.Array))
			structure.Read(r);
	}
}

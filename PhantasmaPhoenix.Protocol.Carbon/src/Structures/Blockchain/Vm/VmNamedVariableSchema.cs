namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

public struct VmNamedVariableSchema : ICarbonBlob
{
	public SmallString name;
	public VmVariableSchema schema;

	public void Write(BinaryWriter w)
	{
		w.Write(name);
		w.Write(schema);
	}
	public void Read(BinaryReader r)
	{
		r.Read(out name);
		r.Read(out schema);
	}
}

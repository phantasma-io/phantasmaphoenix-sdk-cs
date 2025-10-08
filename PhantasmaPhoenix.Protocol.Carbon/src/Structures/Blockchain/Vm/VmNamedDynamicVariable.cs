namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

public struct VmNamedDynamicVariable : ICarbonBlob
{
	public SmallString name;
	public VmDynamicVariable value;

	public void Write(BinaryWriter w)
	{
		name.Write(w);
		value.Write(w);
	}
	public void Read(BinaryReader r)
	{
		name.Read(r);
		value.Read(r);
	}
}

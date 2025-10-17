namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

public struct VmStructSchema : ICarbonBlob
{
	public enum Flags
	{
		None = 0,
		DynamicExtras = 1 << 0,
		IsSorted = 1 << 1,
	};
	public VmNamedVariableSchema[] fields;
	public Flags flags;

	//VmNamedVariableSchema operator[](SmallString);

	//static VmStructSchema Sort(VmNamedVariableSchema[] fields, bool allowDynamicExtras)
	//{
	//	fields.OrderBy(x => x.name)
	//}

	public static VmStructSchema CreateEmpty()
	{
		return new VmStructSchema
		{
			fields = new VmNamedVariableSchema[0],
			flags = Flags.None
		};
	}

	public void Write(BinaryWriter w)
	{
		w.WriteArray(fields);
		w.Write1(flags);
	}
	public void Read(BinaryReader r)
	{
		r.ReadArray(out fields);
		r.Read1(out flags);
	}
}

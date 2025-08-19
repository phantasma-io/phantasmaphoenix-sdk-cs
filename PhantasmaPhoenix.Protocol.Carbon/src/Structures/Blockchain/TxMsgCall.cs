namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct TxMsgCall : ICarbonBlob
{
	public UInt32 moduleId;
	public UInt32 methodId;
	public byte[] args;

	public void Write(BinaryWriter w)
	{
		w.Write4(moduleId);
		w.Write4(methodId);
		w.WriteArray(args);
	}

	public void Read(BinaryReader r)
	{
		r.Read4(out moduleId);
		r.Read4(out methodId);
		r.ReadArray(out args);
	}
}

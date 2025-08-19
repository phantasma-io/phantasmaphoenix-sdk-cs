namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct TxMsgCall_Multi : ICarbonBlob
{
	public TxMsgCall[] calls;

	public void Write(BinaryWriter w)
	{
		w.WriteArray(calls);
	}

	public void Read(BinaryReader r)
	{
		r.ReadArray(out calls);
	}
}

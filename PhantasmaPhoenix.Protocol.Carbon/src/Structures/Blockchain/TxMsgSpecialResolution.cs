namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct TxMsgSpecialResolution : ICarbonBlob
{
	public UInt64 resolutionId;
	public TxMsgCall[] calls;

	public void Write(BinaryWriter w)
	{
		w.Write(resolutionId);
		w.WriteArray(calls);
	}

	public void Read(BinaryReader r)
	{
		resolutionId = r.ReadUInt64();
		r.ReadArray(out calls);
	}
}

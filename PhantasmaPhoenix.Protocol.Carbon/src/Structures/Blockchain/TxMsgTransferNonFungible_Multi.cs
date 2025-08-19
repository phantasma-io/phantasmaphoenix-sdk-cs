namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct TxMsgTransferNonFungible_Multi : ICarbonBlob
{
	public Bytes32 to;
	public UInt64 tokenId;
	public UInt64[] instanceIds;

	public void Write(BinaryWriter w)
	{
		w.Write32(to);
		w.Write8(tokenId);
		w.WriteArray64(instanceIds);
	}

	public void Read(BinaryReader r)
	{
		r.Read32(out to);
		r.Read8(out tokenId);
		r.ReadArray64(out instanceIds);
	}
}

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct TxMsgTransferNonFungible_Multi_GasPayer : ICarbonBlob
{
	public Bytes32 to;
	public Bytes32 from;
	public UInt64 tokenId;
	public UInt64[] instanceIds;

	public void Write(BinaryWriter w)
	{
		w.Write32(to);
		w.Write32(from);
		w.Write8(tokenId);
		w.WriteArray64(instanceIds);
	}

	public void Read(BinaryReader r)
	{
		r.Read32(out to);
		r.Read32(out from);
		r.Read8(out tokenId);
		r.ReadArray64(out instanceIds);
	}
}

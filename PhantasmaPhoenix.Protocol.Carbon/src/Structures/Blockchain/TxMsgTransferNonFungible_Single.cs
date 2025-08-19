namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct TxMsgTransferNonFungible_Single : ICarbonBlob
{
	public Bytes32 to;
	public UInt64 tokenId;
	public UInt64 instanceId;

	public void Write(BinaryWriter w)
	{
		w.Write32(to);
		w.Write8(tokenId);
		w.Write8(instanceId);
	}

	public void Read(BinaryReader r)
	{
		r.Read32(out to);
		r.Read8(out tokenId);
		r.Read8(out instanceId);
	}
}

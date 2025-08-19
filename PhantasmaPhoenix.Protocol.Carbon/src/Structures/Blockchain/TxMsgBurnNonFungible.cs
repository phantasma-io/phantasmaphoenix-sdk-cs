namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct TxMsgBurnNonFungible : ICarbonBlob
{
	public UInt64 tokenId;
	public UInt64 instanceId;

	public void Write(BinaryWriter w)
	{
		w.Write8(tokenId);
		w.Write8(instanceId);
	}

	public void Read(BinaryReader r)
	{
		r.Read8(out tokenId);
		r.Read8(out instanceId);
	}
}

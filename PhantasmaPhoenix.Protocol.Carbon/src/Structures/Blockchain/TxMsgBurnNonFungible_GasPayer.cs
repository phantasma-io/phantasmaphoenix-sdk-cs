namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct TxMsgBurnNonFungible_GasPayer : ICarbonBlob
{
	public UInt64 tokenId;
	public Bytes32 from;
	public UInt64 instanceId;

	public void Write(BinaryWriter w)
	{
		w.Write8(tokenId);
		w.Write32(from);
		w.Write8(instanceId);
	}

	public void Read(BinaryReader r)
	{
		r.Read8(out tokenId);
		r.Read32(out from);
		r.Read8(out instanceId);
	}
}

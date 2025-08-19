namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct TxMsgMintFungible : ICarbonBlob
{
	public UInt64 tokenId;
	public Bytes32 to;
	public IntX amount;

	public void Write(BinaryWriter w)
	{
		w.Write8(tokenId);
		w.Write32(to);
		w.Write(amount);
	}

	public void Read(BinaryReader r)
	{
		r.Read8(out tokenId);
		r.Read32(out to);
		r.Read(out amount);
	}
}

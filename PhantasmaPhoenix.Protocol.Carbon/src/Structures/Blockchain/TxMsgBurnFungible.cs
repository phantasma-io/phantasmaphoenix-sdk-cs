namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct TxMsgBurnFungible : ICarbonBlob
{
	public UInt64 tokenId;
	public IntX amount;

	public void Write(BinaryWriter w)
	{
		w.Write8(tokenId);
		w.Write(amount);
	}

	public void Read(BinaryReader r)
	{
		r.Read8(out tokenId);
		r.Read(out amount);
	}
}

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct TxMsgBurnFungible_GasPayer : ICarbonBlob
{
	public UInt64 tokenId;
	public Bytes32 from;
	public IntX amount;

	public void Write(BinaryWriter w)
	{
		w.Write8(tokenId);
		w.Write32(from);
		w.Write(amount);
	}

	public void Read(BinaryReader r)
	{
		r.Read8(out tokenId);
		r.Read32(out from);
		r.Read(out amount);
	}
}

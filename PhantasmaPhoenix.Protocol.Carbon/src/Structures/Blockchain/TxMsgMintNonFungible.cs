namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct TxMsgMintNonFungible : ICarbonBlob
{
	public UInt64 tokenId;
	public Bytes32 to;
	public UInt32 seriesId;
	public byte[] rom;
	public byte[] ram;

	public void Write(BinaryWriter w)
	{
		w.Write8(tokenId);
		w.Write32(to);
		w.Write4(seriesId);
		w.WriteArray(rom);
		w.WriteArray(ram);
	}

	public void Read(BinaryReader r)
	{
		r.Read8(out tokenId);
		r.Read32(out to);
		r.Read4(out seriesId);
		r.ReadArray(out rom);
		r.ReadArray(out ram);
	}
}

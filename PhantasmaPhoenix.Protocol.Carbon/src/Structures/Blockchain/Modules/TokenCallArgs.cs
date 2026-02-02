namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;

/*
 * Token contract call arguments for ModuleId.Token. The field order must
 * match the serialized call argument layout.
 */
public struct NftMintInfo : ICarbonBlob
{
	public UInt32 seriesId;
	public byte[] rom;
	public byte[] ram;

	public void Write(BinaryWriter w)
	{
		w.Write4(seriesId);
		w.WriteArray(rom);
		w.WriteArray(ram);
	}

	public void Read(BinaryReader r)
	{
		r.Read4(out seriesId);
		r.ReadArray(out rom);
		r.ReadArray(out ram);
	}
}

public struct MintNonFungibleArgs : ICarbonBlob
{
	public UInt64 tokenId;
	public Bytes32 address;
	public NftMintInfo[] tokens;

	public void Write(BinaryWriter w)
	{
		w.Write8(tokenId);
		w.Write32(address);
		w.WriteArray(tokens);
	}

	public void Read(BinaryReader r)
	{
		r.Read8(out tokenId);
		r.Read32(out address);
		r.ReadArray(out tokens);
	}
}

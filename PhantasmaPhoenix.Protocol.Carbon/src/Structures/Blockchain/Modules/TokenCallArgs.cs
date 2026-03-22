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

// Deterministic Phantasma mint resolves the target series by its Phantasma metadata `_i`.
public struct PhantasmaNftMintInfo : ICarbonBlob
{
	public IntX phantasmaSeriesId;
	public byte[] rom; // public schema-driven NFT payload; chain-owned `_i` / nested `rom` are not caller input here
	public byte[] ram;

	public void Write(BinaryWriter w)
	{
		w.Write(phantasmaSeriesId);
		w.WriteArray(rom);
		w.WriteArray(ram);
	}

	public void Read(BinaryReader r)
	{
		r.Read(out phantasmaSeriesId);
		r.ReadArray(out rom);
		r.ReadArray(out ram);
	}
}

public struct MintPhantasmaNonFungibleArgs : ICarbonBlob
{
	public UInt64 tokenId;
	public Bytes32 address;
	public PhantasmaNftMintInfo[] tokens;

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

// Deterministic Phantasma mint returns the derived Phantasma `_i` together with the Carbon instance id.
public struct PhantasmaNftMintResult : ICarbonBlob
{
	public Bytes32 phantasmaNftId;
	public UInt64 carbonInstanceId;

	public void Write(BinaryWriter w)
	{
		w.Write(phantasmaNftId);
		w.Write8(carbonInstanceId);
	}

	public void Read(BinaryReader r)
	{
		r.Read(out phantasmaNftId);
		r.Read8(out carbonInstanceId);
	}
}

public struct TransferFungibleArgs : ICarbonBlob
{
	public Bytes32 to;
	public Bytes32 from;
	public UInt64 tokenId;
	public IntX amount;

	public void Write(BinaryWriter w)
	{
		w.Write32(to);
		w.Write32(from);
		w.Write8(tokenId);
		w.Write(amount);
	}

	public void Read(BinaryReader r)
	{
		r.Read32(out to);
		r.Read32(out from);
		r.Read8(out tokenId);
		r.Read(out amount);
	}
}

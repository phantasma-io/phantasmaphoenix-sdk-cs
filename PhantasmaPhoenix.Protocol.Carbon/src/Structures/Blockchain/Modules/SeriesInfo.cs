using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;

public struct SeriesInfo : ICarbonBlob
{
	public uint maxMint;
	public uint maxSupply;
	public Bytes32 owner;
	public byte[] metadata; // TokenInfo.tokenSchemas.seriesMetadata
	public VmStructSchema rom;
	public VmStructSchema ram;

	public void Write(BinaryWriter w)
	{
		w.Write4(maxMint);
		w.Write4(maxSupply);
		w.Write(owner);
		w.WriteArray(metadata);
		w.Write(rom);
		w.Write(ram);
	}
	public void Read(BinaryReader r)
	{
		r.Read4(out maxMint);
		r.Read4(out maxSupply);
		r.Read(out owner);
		r.ReadArray(out metadata);
		r.Read(out rom);
		r.Read(out ram);
	}
};

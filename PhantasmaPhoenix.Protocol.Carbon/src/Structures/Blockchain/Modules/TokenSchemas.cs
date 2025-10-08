using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;

public struct TokenSchemas : ICarbonBlob
{
	public VmStructSchema seriesMetadata;
	public VmStructSchema rom;
	public VmStructSchema ram;

	public void Write(BinaryWriter w)
	{
		w.Write(seriesMetadata);
		w.Write(rom);
		w.Write(ram);
	}
	public void Read(BinaryReader r)
	{
		r.Read(out seriesMetadata);
		r.Read(out rom);
		r.Read(out ram);
	}
};

namespace PhantasmaPhoenix.Protocol.Carbon;

public struct Witness : ICarbonBlob
{
	public Bytes32 address;
	public Bytes64 signature;

	public void Write(BinaryWriter w)
	{
		w.Write32(address);
		w.Write64(signature);
	}

	public void Read(BinaryReader r)
	{
		r.Read32(out address);
		r.Read64(out signature);
	}
}

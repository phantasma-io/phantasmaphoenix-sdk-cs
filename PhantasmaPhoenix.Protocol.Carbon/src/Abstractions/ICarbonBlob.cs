namespace PhantasmaPhoenix.Protocol.Carbon;

public interface ICarbonBlob
{
	public void Write(BinaryWriter w);
	public void Read(BinaryReader r);
}

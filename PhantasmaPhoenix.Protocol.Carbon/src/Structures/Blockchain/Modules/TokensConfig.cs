namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;

public struct TokensConfig : ICarbonBlob
{
	public TokensConfigFlags flags;

	public void Write(BinaryWriter w)
	{
		w.Write1(flags);
	}

	public void Read(BinaryReader r)
	{
		r.Read1(out flags);
	}
}

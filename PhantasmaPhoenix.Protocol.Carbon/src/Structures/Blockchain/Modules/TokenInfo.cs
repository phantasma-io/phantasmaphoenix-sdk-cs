namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;

public struct TokenInfo : ICarbonBlob
{
	public IntX maxSupply;
	public TokenFlags flags;
	public uint decimals;
	public Bytes32 owner;
	public SmallString symbol;
	public byte[] metadata;
	public byte[] tokenSchemas;

	public void Write(BinaryWriter w)
	{
		w.Write(maxSupply);
		w.Write1(flags);
		w.Write1(decimals);
		w.Write32(owner);
		w.Write(symbol);
		w.WriteArray(metadata);
		if ((flags & TokenFlags.NonFungible) != 0)
			w.WriteArray(tokenSchemas);
	}
	public void Read(BinaryReader r)
	{
		r.Read(out maxSupply);
		r.Read1(out flags);
		r.Read1(out decimals);
		r.Read32(out owner);
		r.Read(out symbol);
		r.ReadArray(out metadata);
		if ((flags & TokenFlags.NonFungible) != 0)
			r.ReadArray(out tokenSchemas);
	}
}

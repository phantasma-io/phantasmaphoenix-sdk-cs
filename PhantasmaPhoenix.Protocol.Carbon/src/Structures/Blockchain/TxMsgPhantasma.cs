namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct TxMsgPhantasma : ICarbonBlob
{
	public SmallString nexus;
	public SmallString chain;
	public byte[] script;

	public void Write(BinaryWriter w)
	{
		w.Write(nexus);
		w.Write(chain);
		w.WriteArray(script);
	}

	public void Read(BinaryReader r)
	{
		r.Read(out nexus);
		r.Read(out chain);
		r.ReadArray(out script);
	}
}

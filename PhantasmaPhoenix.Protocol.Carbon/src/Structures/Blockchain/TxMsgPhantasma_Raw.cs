namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct TxMsgPhantasma_Raw : ICarbonBlob
{
	public byte[] transaction;

	public void Write(BinaryWriter w)
	{
		w.WriteArray(transaction);
	}

	public void Read(BinaryReader r)
	{
		r.ReadArray(out transaction);
	}
}

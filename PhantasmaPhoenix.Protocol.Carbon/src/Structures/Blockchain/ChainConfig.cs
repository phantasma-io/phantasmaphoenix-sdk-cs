namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct ChainConfig : ICarbonBlob
{
	public Byte version;
	public Byte reserved1;
	public Byte reserved2;
	public Byte reserved3;
	public UInt32 allowedTxTypes;
	public UInt32 expiryWindow;
	public UInt32 blockRateTarget;

	public void Write(BinaryWriter w)
	{
		w.Write1(version);
		w.Write1(reserved1);
		w.Write1(reserved2);
		w.Write1(reserved3);
		w.Write4(allowedTxTypes);
		w.Write4(expiryWindow);
		w.Write4(blockRateTarget);
	}

	public void Read(BinaryReader r)
	{
		r.Read1(out version);
		r.Read1(out reserved1);
		r.Read1(out reserved2);
		r.Read1(out reserved3);
		r.Read4(out allowedTxTypes);
		r.Read4(out expiryWindow);
		r.Read4(out blockRateTarget);
	}
}

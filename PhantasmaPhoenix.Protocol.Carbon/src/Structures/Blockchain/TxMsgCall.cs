namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct TxMsgCall : ICarbonBlob
{
	public UInt32 moduleId;
	public UInt32 methodId;
	public byte[] args;
	public MsgCallArgSections sections;

	public void Write(BinaryWriter w)
	{
		w.Write4(moduleId);
		w.Write4(methodId);
		if (sections.HasSections)
		{
			sections.Write(w);
			return;
		}

		var data = args ?? Array.Empty<byte>();
		w.Write4(data.Length);
		if (data.Length > 0)
		{
			w.Write(data);
		}
	}

	public void Read(BinaryReader r)
	{
		r.Read4(out moduleId);
		r.Read4(out methodId);
		int length;
		r.Read4(out length);
		if (length >= 0)
		{
			args = r.ReadExactly(length);
			sections = default;
			return;
		}

		args = Array.Empty<byte>();
		sections = new MsgCallArgSections();
		sections.ReadWithNegativeCount(r, length);
	}
}

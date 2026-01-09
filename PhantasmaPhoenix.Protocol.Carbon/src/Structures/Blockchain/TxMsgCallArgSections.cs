using System.IO;
using PhantasmaPhoenix.Core;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct MsgCallArgs : ICarbonBlob
{
	public int registerOffset;
	public byte[] args;

	public void Write(BinaryWriter w)
	{
		if (registerOffset < 0)
		{
			w.Write4(registerOffset);
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
		int value;
		r.Read4(out value);
		if (value < 0)
		{
			registerOffset = value;
			args = Array.Empty<byte>();
			return;
		}

		registerOffset = 0;
		args = r.ReadExactly(value);
	}
}

public struct MsgCallArgSections : ICarbonBlob
{
	public MsgCallArgs[] argSections;

	public bool HasSections => argSections != null && argSections.Length > 0;

	public void Write(BinaryWriter w)
	{
		Throw.If(argSections == null || argSections.Length == 0, "arg sections are empty");
		w.Write4(-argSections.Length);
		foreach (var section in argSections)
		{
			section.Write(w);
		}
	}

	public void Read(BinaryReader r)
	{
		int count;
		r.Read4(out count);
		ReadWithNegativeCount(r, count);
	}

	public void ReadWithNegativeCount(BinaryReader r, int countNegative)
	{
		Throw.If(countNegative >= 0, "arg sections count must be negative");
		var length = -countNegative;
		if (length == 0)
		{
			argSections = Array.Empty<MsgCallArgs>();
			return;
		}

		argSections = new MsgCallArgs[length];
		for (int i = 0; i < length; i++)
		{
			var section = new MsgCallArgs();
			section.Read(r);
			argSections[i] = section;
		}
	}
}

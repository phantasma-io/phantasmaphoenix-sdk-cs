using System.Text;
using PhantasmaPhoenix.Core;

namespace PhantasmaPhoenix.Protocol.Carbon;

public struct SmallString : ICarbonBlob
{
	public string data;

	public SmallString() { data = ""; }

	public SmallString(string s)
	{
		data = s;
		Throw.If(Encoding.UTF8.GetByteCount(data) > 255, "SmallString was too long");
	}
	public byte[] GetBytes()
	{
		return Encoding.UTF8.GetBytes(data);
	}
	public static SmallString FromBytes(byte[] bytes)
	{
		Throw.Assert(bytes.Length < 256);
		return new SmallString { data = Encoding.UTF8.GetString(bytes) };
	}
	public static SmallString FromBytes(byte[] bytes, int index, int count)
	{
		Throw.Assert(count < 256);
		return new SmallString { data = Encoding.UTF8.GetString(bytes, index, count) };
	}

	public int CompareTo(SmallString other) { return data.CompareTo(other.data); }
	//public static bool operator ==(SmallString a, SmallString b) { return a.data == b.data;  }
	//public static bool operator !=(SmallString a, SmallString b) { return a.data != b.data;  }

	public void Write(BinaryWriter w)
	{
		byte[] bytes = GetBytes();
		Throw.If(bytes.Length > 255, "SmallString was too long");
		w.Write((byte)bytes.Length);
		w.Write(bytes);
	}

	public void Read(BinaryReader r)
	{
		byte length = r.ReadByte();
		byte[] bytes = r.ReadExactly(length);
		data = Encoding.UTF8.GetString(bytes);
	}

	public readonly static SmallString Empty = new SmallString { data = "" };
}

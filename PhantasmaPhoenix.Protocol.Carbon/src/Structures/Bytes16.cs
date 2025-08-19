using PhantasmaPhoenix.Core;

namespace PhantasmaPhoenix.Protocol.Carbon;

public struct Bytes16 : ICarbonBlob
{
	public readonly static Bytes16 Empty = new();

	public byte[] bytes;

	public Bytes16()
	{
		bytes = new byte[16];
	}

	public Bytes16(byte[] data)
	{
		Throw.If(data.Length != 16, "");
		bytes = data;
	}

	public override int GetHashCode()
	{
		return BitConverter.ToInt32(bytes, 0);
	}

	public override bool Equals(object? obj)
	{
		if (obj == null || !(obj is Bytes16))
			return false;
		return ((Bytes16)obj).bytes.SequenceEqual(bytes);
	}

	public bool Equals(Bytes16 obj)
	{
		return obj.bytes.SequenceEqual(bytes);
	}

	public void Write(BinaryWriter w)
	{
		w.Write16(bytes);
	}

	public void Read(BinaryReader r)
	{
		r.Read16(out bytes);
	}

	public static explicit operator byte[](Bytes16 v)
	{
		return v.bytes;
	}
}

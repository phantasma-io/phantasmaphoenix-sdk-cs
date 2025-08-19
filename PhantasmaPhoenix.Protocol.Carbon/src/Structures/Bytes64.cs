using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Core.Extensions;

namespace PhantasmaPhoenix.Protocol.Carbon;

public struct Bytes64 : ICarbonBlob
{
	public readonly static Bytes64 Empty = new();

	public byte[] bytes;

	public Bytes64()
	{
		bytes = new byte[64];
	}

	public Bytes64(byte[] data)
	{
		Throw.If(data.Length != 64, "");
		bytes = data;
	}

	public string ToHex()
	{
		return bytes.ToHex();
	}

	public override int GetHashCode()
	{
		return BitConverter.ToInt32(bytes, 0);
	}

	public override bool Equals(object? obj)
	{
		if (obj == null || !(obj is Bytes64))
			return false;
		return ((Bytes64)obj).bytes.SequenceEqual(bytes);
	}

	public bool Equals(Bytes64 obj)
	{
		return obj.bytes.SequenceEqual(bytes);
	}

	public void Write(BinaryWriter w)
	{
		w.Write64(bytes);
	}

	public void Read(BinaryReader r)
	{
		r.Read64(out bytes);
	}

	public static explicit operator byte[](Bytes64 v)
	{
		return v.bytes;
	}
}

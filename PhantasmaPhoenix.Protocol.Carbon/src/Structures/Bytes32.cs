using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Core.Extensions;

namespace PhantasmaPhoenix.Protocol.Carbon;

public struct Bytes32 : ICarbonBlob
{
	public readonly static Bytes32 Empty = new();

	public byte[] bytes;

	public Bytes32()
	{
		bytes = new byte[32];
	}

	public Bytes32(byte[] data, int offset)
	{
		Throw.If(data.Length - offset < 32, "");
		bytes = new byte[32];
		Array.Copy(data, offset, bytes, 0, 32);
	}

	public Bytes32(byte[] data)
	{
		Throw.If(data.Length != 32, "");
		bytes = data;
	}

	public Bytes32(string hex) : this(hex.FromHex() ?? Array.Empty<byte>())
	{
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
		if (obj == null || !(obj is Bytes32))
			return false;
		return ((Bytes32)obj).bytes.SequenceEqual(bytes);
	}

	public bool Equals(Bytes32 obj)
	{
		return obj.bytes.SequenceEqual(bytes);
	}

	public void Write(BinaryWriter w)
	{
		w.Write32(bytes);
	}

	public void Read(BinaryReader r)
	{
		r.Read32(out bytes);
	}

	public static explicit operator byte[](Bytes32 v)
	{
		return v.bytes;
	}
}

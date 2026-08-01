using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Core.Extensions;

namespace PhantasmaPhoenix.Protocol.Carbon;

// Same equality contract as Bytes32: see the comments there for why IEquatable and a full-content hash
// are required rather than optional.
public struct Bytes64 : ICarbonBlob, IEquatable<Bytes64>
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
		return ByteArrayUtils.HashBytes(bytes);
	}

	public override bool Equals(object? obj)
	{
		return obj is Bytes64 other && Equals(other);
	}

	public bool Equals(Bytes64 obj)
	{
		return AsSpan().SequenceEqual(obj.AsSpan());
	}

	private readonly ReadOnlySpan<byte> AsSpan()
	{
		return bytes;
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

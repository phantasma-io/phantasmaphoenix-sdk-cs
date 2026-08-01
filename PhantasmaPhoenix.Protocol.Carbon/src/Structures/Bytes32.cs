using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Core.Extensions;

namespace PhantasmaPhoenix.Protocol.Carbon;

// IEquatable is part of the contract, not an optimization: without it Dictionary/HashSet fall back to
// the object comparer, which boxes both values on every single comparison.
public struct Bytes32 : ICarbonBlob, IEquatable<Bytes32>
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

	// Hash the whole value, not a prefix. These values are used as dictionary keys (transaction ids,
	// node ids, balance owners), and any set of values sharing their first bytes - which a caller can
	// choose - would otherwise collapse into one bucket and turn every lookup into a linear scan.
	// A default-initialized value has no backing array; it hashes as empty instead of throwing.
	public override int GetHashCode()
	{
		return ByteArrayUtils.HashBytes(bytes);
	}

	public override bool Equals(object? obj)
	{
		return obj is Bytes32 other && Equals(other);
	}

	public bool Equals(Bytes32 obj)
	{
		return AsSpan().SequenceEqual(obj.AsSpan());
	}

	// Span comparison instead of LINQ SequenceEqual: the LINQ overload allocates two enumerators per
	// call and walks the bytes one interface call at a time, on a path that runs per dictionary probe.
	private readonly ReadOnlySpan<byte> AsSpan()
	{
		return bytes;
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

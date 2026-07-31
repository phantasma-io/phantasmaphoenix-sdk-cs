using PhantasmaPhoenix.Core.Extensions;

namespace PhantasmaPhoenix.Core;

/// <summary>
/// Opaque bytes that travel as an uppercase hex string.
/// </summary>
/// <remarks>
/// The wire shape is unchanged and always was a hex string; what this type changes is that a producer
/// no longer has to MATERIALIZE that string in order to answer with it. A 28.9 MB transaction message
/// becomes 57.8 million hex characters - 115.6 MB of UTF-16 on the large object heap - before one byte
/// reaches the socket. Holding the bytes and letting the serializer write the hex in fixed-size pieces
/// removes that allocation entirely (see <see cref="IHexValueWriter"/>).
///
/// The implicit conversion to string keeps consumer code compiling and answering exactly what it did
/// before. That conversion is where the large string is built, so a consumer that wants the bytes
/// should call <see cref="ToArray"/> and skip the round trip through hex.
/// </remarks>
public sealed class HexBytes : IEquatable<HexBytes>
{
	private readonly byte[] _array;
	private readonly int _offset;
	private readonly int _count;

	private HexBytes(byte[] array, int offset, int count)
	{
		_array = array;
		_offset = offset;
		_count = count;
	}

	/// <summary>Number of bytes carried, i.e. half the length of the hex string on the wire.</summary>
	public int ByteLength => _count;

	/// <summary>
	/// Wraps a region of an existing buffer WITHOUT copying it. The caller must not write to that
	/// region afterwards - this type is used to avoid copies of multi-megabyte payloads, so defensive
	/// copying here would defeat its purpose.
	/// </summary>
	public static HexBytes FromBytes(byte[] array, int offset, int count)
	{
		if (array == null) throw new ArgumentNullException(nameof(array));
		if (offset < 0 || count < 0 || offset + count > array.Length)
			throw new ArgumentOutOfRangeException(nameof(count), "The region does not fit the buffer.");

		return new HexBytes(array, offset, count);
	}

	public static HexBytes FromBytes(byte[] array) =>
		FromBytes(array ?? throw new ArgumentNullException(nameof(array)), 0, array.Length);

	/// <summary>Parses a hex string; null in, null out, so an absent field stays absent.</summary>
	public static HexBytes? FromHex(string? hex)
	{
		if (hex == null) return null;
		if (hex.Length == 0) return FromBytes(Array.Empty<byte>());

		var bytes = hex.FromHex() ?? throw new FormatException("Value is not a hex string.");
		return FromBytes(bytes);
	}

	/// <summary>Copies the bytes out. This is the cheap path - it never builds the hex string.</summary>
	public byte[] ToArray()
	{
		var copy = new byte[_count];
		Buffer.BlockCopy(_array, _offset, copy, 0, _count);
		return copy;
	}

	/// <summary>Writes the bytes into <paramref name="writer"/> as a hex string value.</summary>
	public void WriteTo(IHexValueWriter writer)
	{
		if (writer == null) throw new ArgumentNullException(nameof(writer));
		writer.WriteHexValue(_array, _offset, _count);
	}

	/// <summary>The hex string exactly as it appears on the wire: uppercase, no separators.</summary>
	public override string ToString() => _array.ToHex(_offset, _count);

	public static implicit operator string?(HexBytes? value) => value?.ToString();

	public bool Equals(HexBytes? other)
	{
		if (other is null) return false;
		if (ReferenceEquals(this, other)) return true;
		if (_count != other._count) return false;

		for (var i = 0; i < _count; i++)
		{
			if (_array[_offset + i] != other._array[other._offset + i]) return false;
		}

		return true;
	}

	public override bool Equals(object? obj) => Equals(obj as HexBytes);

	public override int GetHashCode()
	{
		// FNV-1a over at most the first 64 bytes plus the length: hashing a multi-megabyte payload in
		// full would make dictionary use quietly expensive, and the length separates the common cases.
		unchecked
		{
			var hash = (int)2166136261;
			hash = (hash ^ _count) * 16777619;

			var limit = Math.Min(_count, 64);
			for (var i = 0; i < limit; i++)
			{
				hash = (hash ^ _array[_offset + i]) * 16777619;
			}

			return hash;
		}
	}
}

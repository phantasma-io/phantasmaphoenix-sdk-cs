using System.Linq.Expressions;
using System.Text;
using System.Numerics;
using PhantasmaPhoenix.Core;

namespace PhantasmaPhoenix.Protocol.Carbon;

// Serialization algorithms for Carbon core data structures should be *simple* and *explicit* for clarity.
// Fixed size fields are used extensively.

public static class BinaryStreamExt
{
	public static byte[] ReadExactly(this BinaryReader r, int count)
	{
		var result = r.ReadBytes(count);
		Throw.If(count != result.Length, "end of stream reached");
		return result;
	}

	public static void Read16(this BinaryReader r, out byte[] v)
	{
		v = r.ReadExactly(16);
	}

	public static void Read32(this BinaryReader r, out byte[] v)
	{
		v = r.ReadExactly(32);
	}

	public static void Read64(this BinaryReader r, out byte[] v)
	{
		v = r.ReadExactly(64);
	}

	public static void Read16(this BinaryReader r, out Bytes16 v)
	{
		v.bytes = r.ReadExactly(16);
	}

	public static void Read32(this BinaryReader r, out Bytes32 v)
	{
		v.bytes = r.ReadExactly(32);
	}

	public static void Read64(this BinaryReader r, out Bytes64 v)
	{
		v.bytes = r.ReadExactly(64);
	}

	public static Bytes16 Read16(this BinaryReader r)
	{
		return CarbonBlob.New<Bytes16>(r);
	}

	public static Bytes32 Read32(this BinaryReader r)
	{
		return CarbonBlob.New<Bytes32>(r);
	}

	public static Bytes64 Read64(this BinaryReader r)
	{
		return CarbonBlob.New<Bytes64>(r);
	}

	public static void WriteExactly(this BinaryWriter w, byte[] data, int count)
	{
		Throw.If(count != data.Length, "incorrect input size");
		w.Write(data);
	}

	public static void Write16(this BinaryWriter w, byte[] data)
	{
		w.WriteExactly(data, 16);
	}

	public static void Write32(this BinaryWriter w, byte[] data)
	{
		w.WriteExactly(data, 32);
	}

	public static void Write64(this BinaryWriter w, byte[] data)
	{
		w.WriteExactly(data, 64);
	}

	public static void Write16(this BinaryWriter w, Bytes16 data)
	{
		w.WriteExactly(data.bytes, 16);
	}

	public static void Write32(this BinaryWriter w, Bytes32 data)
	{
		w.WriteExactly(data.bytes, 32);
	}

	public static void Write64(this BinaryWriter w, Bytes64 data)
	{
		w.WriteExactly(data.bytes, 64);
	}

	public static void Write1<T>(this BinaryWriter w, T data) where T : IComparable, IFormattable, IConvertible
	{
		w.Write((byte)Convert.ChangeType(data, typeof(byte)));
	}

	public static void Write1(this BinaryWriter w, Byte data)
	{
		w.Write(data);
	}

	public static void Write2(this BinaryWriter w, Int16 data)
	{
		w.Write(data);
	}

	public static void Write4<T>(this BinaryWriter w, T data) where T : IComparable, IFormattable, IConvertible
	{
		w.Write((Int32)Convert.ChangeType(data, typeof(Int32)));
	}

	public static void Write4(this BinaryWriter w, Int32 data)
	{
		w.Write(data);
	}

	public static void Write4(this BinaryWriter w, UInt32 data)
	{
		w.Write(data);
	}

	public static void Write8(this BinaryWriter w, Int64 data)
	{
		w.Write(data);
	}

	public static void Write8(this BinaryWriter w, UInt64 data)
	{
		w.Write(data);
	}

	public static void Read1<T>(this BinaryReader r, out T v) where T : struct
	{
		v = GenericCastTo<T>.From(r.ReadByte());
	}

	public static void Read1(this BinaryReader r, out Byte v)
	{
		v = r.ReadByte();
	}
	public static Byte Read1(this BinaryReader r)
	{
		return r.ReadByte();
	}

	public static void Read2(this BinaryReader r, out Int16 v)
	{
		v = r.ReadInt16();
	}
	public static Int16 Read2(this BinaryReader r)
	{
		return r.ReadInt16();
	}

	public static void Read4<T>(this BinaryReader r, out T v) where T : struct
	{
		v = GenericCastTo<T>.From(r.ReadInt32());
	}

	public static void Read4(this BinaryReader r, out Int32 v)
	{
		v = r.ReadInt32();
	}
	public static Int32 Read4(this BinaryReader r)
	{
		return r.ReadInt32();
	}

	public static void Read4(this BinaryReader r, out UInt32 v)
	{
		v = r.ReadUInt32();
	}

	public static void Read8(this BinaryReader r, out Int64 v)
	{
		v = r.ReadInt64();
	}
	public static Int64 Read8(this BinaryReader r)
	{
		return r.ReadInt64();
	}

	public static void Read8(this BinaryReader r, out UInt64 v)
	{
		v = r.ReadUInt64();
	}

	public static byte[] ReadRemaining(this BinaryReader r)
	{
		using (var ms = new MemoryStream())
		{
			r.BaseStream.CopyTo(ms);
			return ms.ToArray();
		}
	}

	public static void WriteBigInt(this BinaryWriter w, BigInteger data)
	{
		w.WriteBigInt(data.ToByteArray());
	}
	public static void WriteBigInt(this BinaryWriter w, byte[] bytes)
	{
		// Protocol BigInt bytes must match the validator runtime, not BigInteger's sign-safe minimal form.
		// Normalize to a fixed 32-byte two's-complement word first, then trim trailing fill bytes exactly
		// the way the validator does when emitting uint256/int256 on the wire.
		byte[] word = NormalizeBigIntWord(bytes);
		// The highest bit of the reconstructed 256-bit word determines which byte is treated as sign fill
		// when the validator removes omitted high bytes.
		byte fill = (word[31] & 0x80) != 0 ? (byte)0xFF : (byte)0x00;
		// Unlike the previous SDK logic, validator format does not preserve an extra sign-guard byte.
		// It simply strips all contiguous high fill bytes from the fixed-width word.
		int length = ComputeBigIntSerializedLength(word, fill);
		// Header layout matches the validator:
		// bit 7 = sign of the full 256-bit value, bit 6 = reserved (must stay 0), bits 0-5 = payload length.
		int header = (length & 0x3F) | (fill & 0x80);
		w.Write1(header);
		if (length > 0)
		{
			// Wire payload is always the low-order bytes of the reconstructed 256-bit word.
			w.Write(word, 0, length);
		}
	}

	public static BigInteger ReadBigInt(this byte[] data)
	{
		using (MemoryStream s = new MemoryStream(data))
		{
			BinaryReader r = new(s);
			return r.ReadBigInt();
		}
	}
	public static BigInteger ReadBigInt(this BinaryReader r, out BigInteger data, int preReadHeader = -1)
	{
		// Header layout:
		// bit 7 = sign bit of the full 256-bit value,
		// bit 6 = reserved and must remain 0,
		// bits 0-5 = number of serialized low-order bytes that follow.
		byte header = preReadHeader < 0 ? r.ReadByte() : (byte)(preReadHeader & 0xFF);
		if (header == 0)
			data = BigInteger.Zero;
		else
		{
			int length = header & 0x3F;
			Throw.If((header & 0x40) != 0 || length > 32, "BigInt too big");
			// The omitted high bytes are reconstructed from the header sign bit, exactly like the validator.
			byte fill = (header & 0x80) != 0 ? (byte)0xFF : (byte)0x00;
			// The validator reader reconstructs the full 256-bit word by filling the omitted high bytes.
			// This is required for shortest negative forms such as 0x80, which mean -1 rather than zero.
			byte[] word = new byte[32];
			if (length > 0)
			{
				byte[] bytes = r.ReadExactly(length);
				Array.Copy(bytes, word, length);
			}
			for (int i = length; i < word.Length; i++)
				word[i] = fill;
			// After reconstruction, the sign bit of the highest byte must still agree with the header sign bit.
			// If it does not, the byte sequence is malformed under validator/runtime rules.
			Throw.If((word[31] & 0x80) != (header & 0x80), "non-standard BigInt header");
			data = new BigInteger(word);
		}

		return data;
	}

	private static byte[] NormalizeBigIntWord(byte[] bytes)
	{
		if (bytes.Length == 0)
			return new byte[32];

		// BigInteger may expose an extra sign byte; for protocol purposes we keep only the low 256 bits
		// after confirming the truncated high bytes are pure sign extension.
		byte sourceFill = (bytes[bytes.Length - 1] & 0x80) != 0 ? (byte)0xFF : (byte)0x00;
		if (bytes.Length > 32)
		{
			// Anything above 256 bits must be redundant sign extension. If not, the value does not fit
			// into the protocol's 256-bit integer domain.
			for (int i = 32; i < bytes.Length; i++)
				Throw.Assert(bytes[i] == sourceFill);
			bytes = bytes.Take(32).ToArray();
		}

		byte[] word = new byte[32];
		Array.Copy(bytes, word, bytes.Length);
		// Expand shorter BigInteger output back to a full 256-bit two's-complement word before trimming.
		for (int i = bytes.Length; i < word.Length; i++)
			word[i] = sourceFill;
		return word;
	}

	private static int ComputeBigIntSerializedLength(byte[] word, byte fill)
	{
		int length = word.Length;
		// Validator trimming is intentionally simpler than canonical signed-minimal encoding:
		// once the value is expanded to 256 bits, every trailing fill byte is removed.
		while (length > 0 && word[length - 1] == fill)
			length--;
		return length;
	}

	public static BigInteger ReadBigInt(this BinaryReader r)
	{
		BigInteger result;
		return r.ReadBigInt(out result);
	}

	public static void WriteArrayBigInt(this BinaryWriter w, BigInteger[] data)
	{
		w.Write4(data.Length);
		foreach (var t in data)
			w.WriteBigInt(t);
	}

	public static BigInteger[] ReadArrayBigInt(this BinaryReader r)
	{
		int length;
		r.Read4(out length);
		var data = new BigInteger[length];
		for (int i = 0; i != length; ++i)
			data[i] = r.ReadBigInt();
		return data;
	}

	public static void WriteSz(this BinaryWriter w, string data)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(data);
		foreach (byte b in bytes)
			Throw.Assert(b != 0);
		w.Write(bytes);
		w.Write((byte)0);
	}

	public static void ReadSz(this BinaryReader r, out string data)
	{
		List<byte> result = new();
		for (; ; )
		{
			byte b = r.ReadByte();
			if (b == 0)
				break;
			result.Add(b);
		}

		data = Encoding.UTF8.GetString(result.ToArray());
	}

	public static string ReadSz(this BinaryReader r)
	{
		string sz;
		r.ReadSz(out sz);
		return sz;
	}

	public static void WriteArraySz(this BinaryWriter w, string[] data)
	{
		w.Write4(data.Length);
		foreach (var t in data)
			w.WriteSz(t);
	}

	public static string[] ReadArraySz(this BinaryReader r)
	{
		int length;
		r.Read4(out length);
		var data = new string[length];
		for (int i = 0; i != length; ++i)
			data[i] = r.ReadSz();
		return data;
	}


	public static void WriteArray(this BinaryWriter w, byte[] data)
	{
		w.Write4(data.Length);
		w.Write(data);
	}

	public static void ReadArray(this BinaryReader r, out byte[] data)
	{
		int length;
		r.Read4(out length);
		data = r.ReadExactly(length);
	}

	public static byte[] ReadArray(this BinaryReader r)
	{
		int length;
		r.Read4(out length);
		return r.ReadExactly(length);
	}

	public static void WriteArray64(this BinaryWriter w, UInt64[] data)
	{
		w.Write4(data.Length);
		foreach (var t in data)
			w.Write(t);
	}

	public static void ReadArray64(this BinaryReader r, out UInt64[] data)
	{
		int length;
		r.Read4(out length);
		data = new UInt64[length];
		for (int i = 0; i != length; ++i)
			data[i] = r.ReadUInt64();
	}

	public static void WriteArray64(this BinaryWriter w, Int64[] data)
	{
		w.Write4(data.Length);
		foreach (var t in data)
			w.Write(t);
	}

	public static Int64[] ReadArray64(this BinaryReader r, out Int64[] data)
	{
		int length;
		r.Read4(out length);
		data = new Int64[length];
		for (int i = 0; i != length; ++i)
			data[i] = r.ReadInt64();
		return data;
	}

	public static Int64[] ReadArray64(this BinaryReader r)
	{
		Int64[] v;
		return r.ReadArray64(out v);
	}

	public static void WriteArray32(this BinaryWriter w, Int32[] data)
	{
		w.Write4(data.Length);
		foreach (var t in data)
			w.Write(t);
	}

	public static Int32[] ReadArray32(this BinaryReader r, out Int32[] data)
	{
		int length;
		r.Read4(out length);
		data = new Int32[length];
		for (int i = 0; i != length; ++i)
			data[i] = r.ReadInt32();
		return data;
	}

	public static Int32[] ReadArray32(this BinaryReader r)
	{
		Int32[] v;
		return r.ReadArray32(out v);
	}

	public static void WriteArray16(this BinaryWriter w, Int16[] data)
	{
		w.Write4(data.Length);
		foreach (var t in data)
			w.Write(t);
	}

	public static Int16[] ReadArray16(this BinaryReader r, out Int16[] data)
	{
		int length;
		r.Read4(out length);
		data = new Int16[length];
		for (int i = 0; i != length; ++i)
			data[i] = r.ReadInt16();
		return data;
	}

	public static Int16[] ReadArray16(this BinaryReader r)
	{
		Int16[] v;
		return r.ReadArray16(out v);
	}

	public static void WriteArray8(this BinaryWriter w, sbyte[] data)
	{
		w.Write4(data.Length);
		foreach (var t in data)
			w.Write(t);
	}

	public static sbyte[] ReadArray8(this BinaryReader r, out sbyte[] data)
	{
		int length;
		r.Read4(out length);
		data = new sbyte[length];
		for (int i = 0; i != length; ++i)
			data[i] = r.ReadSByte();
		return data;
	}

	public static sbyte[] ReadArray8(this BinaryReader r)
	{
		sbyte[] v;
		return r.ReadArray8(out v);
	}

	public static void WriteArray(this BinaryWriter w, byte[][] data)
	{
		w.Write4(data.Length);
		foreach (byte[] arr in data)
			w.WriteArray(arr);
	}

	public static void ReadArray(this BinaryReader r, out byte[][] data)
	{
		int length;
		r.Read4(out length);
		data = new byte[length][];
		for (int i = 0; i < length; ++i)
			r.ReadArray(out data[i]);
	}

	public static byte[][] ReadArrayArray(this BinaryReader r)
	{
		byte[][] data;
		r.ReadArray(out data);
		return data;
	}

	public static void Write<T>(this BinaryWriter w, T data) where T : ICarbonBlob
	{
		data.Write(w);
	}

	public static void Read<T>(this BinaryReader r, out T data) where T : ICarbonBlob, new()
	{
		data = new();
		data.Read(r);
	}

	public static void WriteArray<T>(this BinaryWriter w, T[] data) where T : ICarbonBlob
	{
		w.Write4(data.Length);
		foreach (var t in data)
			w.Write(t);
	}

	public static void ReadArray<T>(this BinaryReader r, out T[] data) where T : ICarbonBlob
	{
		int length;
		r.Read4(out length);
		data = new T[length];
		for (int i = 0; i != length; ++i)
			data[i].Read(r);
	}

	public static T[] ReadArray<T>(this BinaryReader r) where T : ICarbonBlob
	{
		T[] data;
		r.ReadArray<T>(out data);
		return data;
	}

	public static T Read<T>(this BinaryReader r) where T : ICarbonBlob, new()
	{
		T t = new T();
		t.Read(r);
		return t;
	}
}

public static class GenericCastTo<T>
{
	public static T From<S>(S s)
	{
		return Cache<S>.caster(s);
	}

	private static class Cache<S>
	{
		public static readonly Func<S, T> caster = Get();

		private static Func<S, T> Get()
		{
			var p = Expression.Parameter(typeof(S));
			var c = Expression.ConvertChecked(p, typeof(T));
			return Expression.Lambda<Func<S, T>>(c, p).Compile();
		}
	}
}

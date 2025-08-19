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
		if (data.IsZero)
		{
			w.Write1(0);
		}
		else
		{
			byte[] bytes = data.ToByteArray();
			if (bytes.Length > 32)
			{
				Throw.Assert(bytes.Length == 33 && (bytes[32] == 0x00 || bytes[32] == 0xFF));
				bytes = bytes.Take(32).ToArray();
				w.WriteBigInt(new BigInteger(bytes));
				return;
			}
			w.WriteBigInt(bytes);
		}
	}
	public static void WriteBigInt(this BinaryWriter w, byte[] bytes)
	{
		if (bytes.Length == 0)
		{
			w.Write1(0);
		}
		else
		{
			int length = bytes.Length;
			Throw.Assert(length <= 32);
			int sign = (bytes[length - 1] & 0x80) == 0 ? 1 : -1;
			int header = (length & 0x3F) | (sign < 0 ? 0x80 : 0);
			w.Write1(header);
			w.WriteExactly(bytes, length);
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
		byte header = preReadHeader < 0 ? r.ReadByte() : (byte)(preReadHeader & 0xFF);
		if (header == 0)
			data = BigInteger.Zero;
		else
		{
			int sign = (header & 0x80) != 0 ? -1 : 1;
			int length = header & 0x3F;
			Throw.If(length > 32, "BigInt too big");
			if (length == 0)
				data = BigInteger.Zero;
			else
			{
				byte[] bytes = r.ReadExactly(length);
				int inherentSign = (bytes[length - 1] & 0x80) != 0 ? -1 : 1;
				if (inherentSign != sign)
				{
					bytes = bytes.Append(sign >= 0 ? (byte)0x00 : (byte)0xFF).ToArray();
				}
				data = new BigInteger(bytes);
				Throw.Assert(data.Sign == sign);
			}
		}

		return data;
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

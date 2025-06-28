using System.Numerics;

namespace PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Hex.HexConvertors.Extensions;

public static class HexBigIntegerConvertorExtensions
{
	public static byte[] ToByteArray(this BigInteger value, bool littleEndian)
	{
		byte[] bytes;
		if (BitConverter.IsLittleEndian != littleEndian)
			bytes = value.ToByteArray().Reverse().ToArray();
		else
			bytes = value.ToByteArray().ToArray();
		return bytes;
	}

	public static string ToHex(this BigInteger value, bool littleEndian, bool compact = true, bool add0x = true)
	{
		if (value.Sign < 0) throw new Exception("Hex Encoding of Negative BigInteger value is not supported");
		if (value == 0) return add0x ? "0x0" : "0";

#if NET7_0_OR_GREATER
		var bytes = value.ToByteArray(true, !littleEndian);
#else
		var bytes = value.ToByteArray();

		// Make unsigned: remove leading sign byte (0x00) if present
#if NETSTANDARD2_0
		if (bytes.Length > 1 && bytes[bytes.Length - 1] == 0x00)
		{
			Array.Resize(ref bytes, bytes.Length - 1);
		}
#else
		if (bytes.Length > 1 && bytes[^1] == 0x00)
		{
			Array.Resize(ref bytes, bytes.Length - 1);
		}
#endif

		// BigEndian required?
		if (BitConverter.IsLittleEndian == littleEndian)
		{
			Array.Reverse(bytes);
		}
#endif

		if (compact)
			return (add0x ? "0x" : "") + bytes.ToHexCompact();

		return (add0x ? "0x" : "") + bytes.ToHex();
	}


	public static BigInteger HexToBigInteger(this string hex, bool isHexLittleEndian)
	{
		if (hex == "0x0") return 0;

		var encoded = hex.HexToByteArray();

		if (BitConverter.IsLittleEndian != isHexLittleEndian)
		{
			var listEncoded = encoded.ToList();
			listEncoded.Insert(0, 0x00);
			encoded = listEncoded.ToArray().Reverse().ToArray();
		}
		return new BigInteger(encoded);
	}
}

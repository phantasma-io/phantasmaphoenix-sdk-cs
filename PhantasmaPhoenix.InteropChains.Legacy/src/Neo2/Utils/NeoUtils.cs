using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Cryptography.Extensions;
using System.Globalization;
using System.Text;

namespace PhantasmaPhoenix.InteropChains.Legacy.Neo2;

public static class NeoUtils
{
	public static bool IsValidAddress(this string address)
	{
		if (string.IsNullOrEmpty(address))
		{
			return false;
		}

		// In Norway "Aa" combination means "å".
		// By default StartsWith() uses current culture for comparison,
		// and if culture is set to Norway, StartWith() believes, that
		// Neo address "Aa..." starts with "å" letter,
		// and address is treated as invalid.
		// We should always use invariant culture for such comparisons.
		if (!address.StartsWith("A", false, CultureInfo.InvariantCulture))
		{
			return false;
		}

		if (address.Length != 34)
		{
			return false;
		}

		byte[] buffer;
		try
		{
			buffer = Base58.Decode(address);

		}
		catch
		{
			return false;
		}

		if (buffer.Length < 4) return false;

		byte[] checksum = buffer.Sha256(0, buffer.Length - 4).Sha256();
		return buffer.Skip(buffer.Length - 4).SequenceEqual(checksum.Take(4));
	}

	public static string ToHexString(this IEnumerable<byte> value)
	{
		StringBuilder sb = new StringBuilder();
		foreach (byte b in value)
			sb.AppendFormat("{0:x2}", b);
		return sb.ToString();
	}

	public static byte[] HexToBytes(this string value)
	{
		if (value == null || value.Length == 0)
			return new byte[0];
		if (value.Length % 2 == 1)
			throw new FormatException();

		if (value.StartsWith("0x"))
		{
			value = value.Substring(2);
		}

		byte[] result = new byte[value.Length / 2];
		for (int i = 0; i < result.Length; i++)
			result[i] = byte.Parse(value.Substring(i * 2, 2), NumberStyles.AllowHexSpecifier);
		return result;
	}

	public static string ToAddress(this UInt160 scriptHash)
	{
		byte[] data = new byte[21];
		data[0] = 23;
		Buffer.BlockCopy(scriptHash.ToArray(), 0, data, 1, 20);
		return data.Base58CheckEncode();
	}

	public static string ToAddressN3(this UInt160 scriptHash)
	{
		byte[] data = new byte[21];
		data[0] = 53;
		Buffer.BlockCopy(scriptHash.ToArray(), 0, data, 1, 20);
		return data.Base58CheckEncode();
	}

	public static UInt160 ToScriptHash(this byte[] script)
	{
		return new UInt160(Hash160(script));
	}

	public static byte[] Hash160(byte[] message)
	{
		return message.Sha256().RIPEMD160();
	}

	private static ThreadLocal<RIPEMD160> _ripemd160 = new ThreadLocal<RIPEMD160>(() => new RIPEMD160());

	public static byte[] AES256Decrypt(this byte[] block, byte[] key)
	{
		using (var aes = System.Security.Cryptography.Aes.Create())
		{
			aes.Key = key;
			aes.Mode = System.Security.Cryptography.CipherMode.ECB;
			aes.Padding = System.Security.Cryptography.PaddingMode.None;
			using (var decryptor = aes.CreateDecryptor())
			{
				return decryptor.TransformFinalBlock(block, 0, block.Length);
			}
		}
	}


	public static byte[] RIPEMD160(this IEnumerable<byte> value)
	{
		return _ripemd160.Value.ComputeHash(value.ToArray());
	}
}

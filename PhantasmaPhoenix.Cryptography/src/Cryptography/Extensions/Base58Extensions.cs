using PhantasmaPhoenix.Core;

namespace PhantasmaPhoenix.Cryptography.Extensions;

public static class Base58Extensions
{
	public static byte[] Base58CheckDecode(this string input)
	{
		byte[] buffer = Base58.Decode(input);

		if (buffer.Length < 4) throw new FormatException();
		byte[] expected_checksum = buffer.Sha256(0, buffer.Length - 4).Sha256();
		expected_checksum = expected_checksum.Take(4).ToArray();
		var src_checksum = buffer.Skip(buffer.Length - 4).ToArray();

		Throw.If(!src_checksum.SequenceEqual(expected_checksum), "WIF checksum failed");
		return buffer.Take(buffer.Length - 4).ToArray();
	}

	public static string Base58CheckEncode(this byte[] data)
	{
		byte[] checksum = data.Sha256().Sha256();
		byte[] buffer = new byte[data.Length + 4];
		Array.Copy(data, 0, buffer, 0, data.Length);
		ByteArrayUtils.CopyBytes(checksum, 0, buffer, data.Length, 4);
		return Base58.Encode(buffer);
	}
}

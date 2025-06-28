using System.Text;
using SHA256 = System.Security.Cryptography.SHA256;

namespace PhantasmaPhoenix.Cryptography.Extensions;

public static class HashExtensions
{
	private static ThreadLocal<SHA256> _sha256 = new ThreadLocal<SHA256>(() => SHA256.Create());

	private static SHA256 sha256 => _sha256.Value == null ? throw new NullReferenceException() : _sha256.Value;

	public static byte[] Sha256(this IEnumerable<byte> value)
	{
		return sha256.ComputeHash(value.ToArray());
	}

	public static byte[] Sha256(this byte[] value, int offset, int count)
	{
		return sha256.ComputeHash(value, offset, count);
	}

	public static byte[] Sha256(this string value)
	{
		var bytes = Encoding.UTF8.GetBytes(value);
		return bytes.Sha256();
	}

	public static byte[] Blake2b_256(this byte[] value, int offset, int count)
	{
#if NETSTANDARD2_0
        var digest = new Org.BouncyCastle.Crypto.Digests.Blake2bDigest(256);
        digest.BlockUpdate(value, offset, count);
        byte[] result = new byte[digest.GetDigestSize()];
        digest.DoFinal(result, 0);
        return result;
#else
		return Blake2Fast.Blake2b.ComputeHash(256 / 8, new ReadOnlySpan<byte>(value, offset, count));
#endif
	}
}

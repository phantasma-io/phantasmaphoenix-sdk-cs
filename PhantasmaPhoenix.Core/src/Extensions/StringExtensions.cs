using System.Text;

namespace PhantasmaPhoenix.Core.Extensions;

public static class StringExtensions
{
	public static byte[] AsByteArray(this string source)
	{
		return Encoding.UTF8.GetBytes(source);
	}
	public static string ToHex(this byte[] value)
	{
#if NET5_0_OR_GREATER
		return Convert.ToHexString(value);
#else
		var sb = new StringBuilder(value.Length * 2);
		for (int i = 0; i < value.Length; i++)
			sb.Append(value[i].ToString("X2"));
		return sb.ToString();
#endif
	}

	public static string ToHex(this byte[] value, int offset, int count)
	{
#if NET5_0_OR_GREATER
		return Convert.ToHexString(value, offset, count);
#else
		var sb = new StringBuilder(count * 2);
		for (int i = offset; i < offset + count; i++)
			sb.Append(value[i].ToString("X2"));
		return sb.ToString();
#endif
	}

#if NET6_0_OR_GREATER
	[return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(value))]
#endif
	public static byte[]? FromHex(this string? value) //todo return not null if value not null
	{
		if (value == null)
			return null;

#if NET5_0_OR_GREATER
		return Convert.FromHexString(value);
#else
		if (value.Length % 2 != 0)
			throw new ArgumentException("Hex string must have an even length", nameof(value));

		var result = new byte[value.Length / 2];
		for (int i = 0; i < result.Length; i++)
		{
			result[i] = Convert.ToByte(value.Substring(i * 2, 2), 16);
		}

		return result;
#endif
	}
}

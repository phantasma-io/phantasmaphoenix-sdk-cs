using System.Runtime.CompilerServices;
using System.Text;

namespace PhantasmaPhoenix.Core.Extensions;

public static class ByteArrayExtensions
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static uint ToUInt32(this byte[] value, int startIndex)
	{
		var a = value[startIndex];
		startIndex++;
		var b = value[startIndex];
		startIndex++;
		var c = value[startIndex];
		startIndex++;
		var d = value[startIndex];
		startIndex++;
		return (uint)(a + (b << 8) + (c << 16) + (d << 24));
	}

	public static string AsString(this byte[] source)
	{
		return Encoding.UTF8.GetString(source);
	}
}

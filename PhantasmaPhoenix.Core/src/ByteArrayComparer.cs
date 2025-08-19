using System.Collections;

namespace PhantasmaPhoenix.Core;

public class ByteArrayComparer : IEqualityComparer<byte[]>, IEqualityComparer, IComparer<byte[]>
{
	int IComparer<byte[]>.Compare(byte[]? x, byte[]? y)
	{
		if (x == null || y == null)
		{
			return (x == null ? 0 : 1) - (y == null ? 0 : 1);
		}
		int minLength = Math.Min(x.Length, y.Length);
		for (int i = 0; i != minLength; ++i)
		{
			if (x[i] < y[i])
				return -1;
			if (x[i] > y[i])
				return 1;
		}
		if (x.Length == y.Length)
			return 0;
		return x.Length < y.Length ? -1 : 1;
	}
	bool IEqualityComparer<byte[]>.Equals(byte[]? left, byte[]? right)
	{
		if (left == null || right == null)
		{
			return left == right;
		}
		return left.SequenceEqual(right);
	}
	bool IEqualityComparer.Equals(object? left, object? right)
	{
		return ((IEqualityComparer<byte[]>)this).Equals((byte[]?)left, (byte[]?)right);
	}
	int IEqualityComparer<byte[]>.GetHashCode(byte[] key)
	{
		if (key == null)
			return 0;
		uint hash = 2166136261;
		for (int i = 0; i < key.Length; i++)
		{
			hash = (16777619 * hash) ^ key[i];
		}
		return (int)hash;
	}
	int IEqualityComparer.GetHashCode(object okey)
	{
		byte[]? key = (byte[]?)okey;
		if (key == null)
			return 0;
		return ((IEqualityComparer<byte[]>)this).GetHashCode(key);
	}
}

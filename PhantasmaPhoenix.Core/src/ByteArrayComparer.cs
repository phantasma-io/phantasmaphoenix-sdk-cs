using System.Collections.Generic;

namespace PhantasmaPhoenix.Core;

public class ByteArrayComparer : IEqualityComparer<byte[]>
{
	public bool Equals(byte[]? left, byte[]? right)
	{
		if (left == null || right == null)
			return left == right;
		return left.CompareBytes(right);
	}

	public int GetHashCode(byte[] key)
	{
		Throw.IfNull(key, nameof(key));
		unchecked // disable overflow, for the unlikely possibility that you
		{
			// are compiling with overflow-checking enabled
			uint hash = 2166136261;
			for (int i = 0; i < key.Length; i++)
			{
				hash = (16777619 * hash) ^ key[i];
			}
			return (int)hash;
		}
	}
}

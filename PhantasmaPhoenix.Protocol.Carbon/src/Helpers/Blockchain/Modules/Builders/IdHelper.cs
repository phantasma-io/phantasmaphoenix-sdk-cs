using System.Numerics;
using PhantasmaPhoenix.Cryptography;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;

public static class IdHelper
{
	public static BigInteger GetRandomPhantasmaId()
	{
		var bytes = Entropy.GetRandomBytes(32);

		// Treat random bytes as a big-endian 256-bit unsigned integer (matches TS helper).
		BigInteger value = BigInteger.Zero;
		foreach (var b in bytes)
		{
			value = (value << 8) | b;
		}

		return value;
	}
}

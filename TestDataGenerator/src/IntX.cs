using System.Numerics;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public static class IntXGenerators
{
	public static string[] IntXGen()
	{
		return [
			"0",
			"1",
			"-1",
			"127",
			"-128",
			"255",
			"-256",
			"32767",
			"-32768",
			"65535",
			"-65536",
			"2147483647",
			"-2147483648",
			"9223372036854775807",          // max int64
			"-9223372036854775808",         // min int64
			"9223372036854775808",          // just above int64 → BigInt
			"-9223372036854775809",         // just below int64 → BigInt
			"18446744073709551615",         // 2^64 - 1
			"-18446744073709551615",        // negative of that

			"340282366920938463463374607431768211455",     // 2^128 - 1
			"-340282366920938463463374607431768211456",    // -2^128

			"-57896044618658097711785492504343953926634992332820282019728792003956564819968", // −2^255 - min value
			"578960446186580977117854925043439539266349923328202820197287920039565648", // 2^255 − 1 - max value

			// Following numbers are beyond limits and produce crash on deserialization:
			// "Constraint failed: invalid intx packing"
			// TODO: Fix handling of numbers above limit
			//"115792089237316195423570985008687907853269984665640564039457584007913129639934",  // 2^256 - 1
			//"-115792089237316195423570985008687907853269984665640564039457584007913129639936", // -2^256
		];
	}
}

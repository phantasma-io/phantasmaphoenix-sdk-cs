﻿using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Hex.HexConvertors.Extensions;

namespace PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Util;

public class Sha3Keccack
{
	public static Sha3Keccack Current { get; } = new Sha3Keccack();

	public string CalculateHash(string value)
	{
		var input = Encoding.UTF8.GetBytes(value);
		var output = CalculateHash(input);
		return output.ToHex();
	}

	public string CalculateHashFromHex(params string[] hexValues)
	{
		var joinedHex = string.Join("", hexValues.Select(x => x.RemoveHexPrefix()).ToArray());
		return CalculateHash(joinedHex.HexToByteArray()).ToHex();
	}

	public byte[] CalculateHash(byte[] value)
	{
		var digest = new KeccakDigest(256);
		var output = new byte[digest.GetDigestSize()];
		digest.BlockUpdate(value, 0, value.Length);
		digest.DoFinal(output, 0);
		return output;
	}
}

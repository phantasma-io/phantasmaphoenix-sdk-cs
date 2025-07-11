﻿using Org.BouncyCastle.Crypto.Digests;

namespace PhantasmaPhoenix.InteropChains.Legacy.Neo2;

public class RIPEMD160
{
	public byte[] ComputeHash(byte[] rgb)
	{
		var digest = new RipeMD160Digest();
		var result = new byte[digest.GetDigestSize()];
		digest.BlockUpdate(rgb, 0, rgb.Length);
		digest.DoFinal(result, 0);

		return result;
	}
}

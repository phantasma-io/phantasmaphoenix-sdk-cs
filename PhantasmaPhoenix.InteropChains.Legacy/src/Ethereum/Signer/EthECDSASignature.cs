﻿using Org.BouncyCastle.Math;
using PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Hex.HexConvertors.Extensions;
using PhantasmaPhoenix.InteropChains.Legacy.Ethereum.RLP;
using PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Signer.Crypto;
using PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Util;

namespace PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Signer;

public class EthECDSASignature
{
	internal EthECDSASignature(BigInteger r, BigInteger s)
	{
		ECDSASignature = new ECDSASignature(r, s);
	}

	public EthECDSASignature(BigInteger r, BigInteger s, byte[] v)
	{
		ECDSASignature = new ECDSASignature(r, s);
		ECDSASignature.V = v;
	}

	internal EthECDSASignature(ECDSASignature signature)
	{
		ECDSASignature = signature;
	}

	internal EthECDSASignature(BigInteger[] rs)
	{
		ECDSASignature = new ECDSASignature(rs);
	}

	public EthECDSASignature(byte[] derSig)
	{
		ECDSASignature = new ECDSASignature(derSig);
	}

	internal ECDSASignature ECDSASignature { get; }

	public byte[] R => ECDSASignature.R.ToByteArrayUnsigned();

	public byte[] S => ECDSASignature.S.ToByteArrayUnsigned();

	public byte[] V
	{
		get => ECDSASignature.V;
		set => ECDSASignature.V = value;
	}

	public bool IsLowS => ECDSASignature.IsLowS;

	public static EthECDSASignature FromDER(byte[] sig)
	{
		return new EthECDSASignature(sig);
	}

	public static string CreateStringSignature(EthECDSASignature signature)
	{
		return "0x" + signature.R.ToHex().PadLeft(64, '0') +
			   signature.S.ToHex().PadLeft(64, '0') +
			   signature.V.ToHex();
	}

	public byte[] ToDER()
	{
		return ECDSASignature.ToDER();
	}

	public bool IsVSignedForChain()
	{
		return V.ToBigIntegerFromRLPDecoded() >= 35;
	}

	public byte[] To64ByteArray()
	{
		var rsigPad = new byte[32];
		Array.Copy(R, 0, rsigPad, rsigPad.Length - R.Length, R.Length);

		var ssigPad = new byte[32];
		Array.Copy(S, 0, ssigPad, ssigPad.Length - S.Length, S.Length);

		return ByteUtil.Merge(rsigPad, ssigPad);
	}

	public static bool IsValidDER(byte[] bytes)
	{
		try
		{
			FromDER(bytes);
			return true;
		}
		catch (FormatException)
		{
			return false;
		}
		catch (Exception)
		{
			//	Utils.error("Unexpected exception in ECDSASignature.IsValidDER " + ex.Message);
			return false;
		}
	}
}

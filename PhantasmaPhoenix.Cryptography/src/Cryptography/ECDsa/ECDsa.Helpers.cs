using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;

namespace PhantasmaPhoenix.Cryptography;

public static class ECDsaHelpers
{
	public static byte[] FromDER(byte[] derSignature, int outputLength = 64)
	{
		if (derSignature.Length < 8 || derSignature[0] != 48) throw new Exception("Invalid ECDSA signature format");

		int offset;
		if (derSignature[1] > 0)
			offset = 2;
		else if (derSignature[1] == 0x81)
			offset = 3;
		else
			throw new Exception("Invalid ECDSA signature format");

		var rLength = derSignature[offset + 1];

		int i = rLength;
		while (i > 0
			   && derSignature[offset + 2 + rLength - i] == 0)
			i--;

		var sLength = derSignature[offset + 2 + rLength + 1];

		int j = sLength;
		while (j > 0
			   && derSignature[offset + 2 + rLength + 2 + sLength - j] == 0)
			j--;

		var rawLen = Math.Max(i, j);
		rawLen = Math.Max(rawLen, outputLength / 2);

		if ((derSignature[offset - 1] & 0xff) != derSignature.Length - offset
			|| (derSignature[offset - 1] & 0xff) != 2 + rLength + 2 + sLength
			|| derSignature[offset] != 2
			|| derSignature[offset + 2 + rLength] != 2)
			throw new Exception("Invalid ECDSA signature format");

		var concatenated = new byte[2 * rawLen];

		Array.Copy(derSignature, offset + 2 + rLength - i, concatenated, rawLen - i, i);
		Array.Copy(derSignature, offset + 2 + rLength + 2 + sLength - j, concatenated, 2 * rawLen - j, j);

		return concatenated;
	}

	public static byte[] ToDER(byte[] signature)
	{
		// We convert from concatenated "raw" R + S format to DER format that Bouncy Castle uses.
		return new DerSequence(
			// first 32 bytes is "R" number
			new DerInteger(new BigInteger(1, signature.Take(32).ToArray())),
			// last 32 bytes is "S" number
			new DerInteger(new BigInteger(1, signature.Skip(32).ToArray())))
			.GetDerEncoded();
	}

	public static X9ECParameters GetECParameters(ECDsaCurve curve)
	{
		return curve switch
		{
			ECDsaCurve.Secp256k1 => ECNamedCurveTable.GetByName("secp256k1"),
			ECDsaCurve.Secp256r1 => ECNamedCurveTable.GetByName("secp256r1"),
			_ => throw new Exception("Unsupported curve"),
		};
	}

	public static ECDomainParameters GetECDomainParameters(ECDsaCurve curve)
	{
		var ecParams = GetECParameters(curve);
		return new ECDomainParameters(ecParams.Curve, ecParams.G, ecParams.N, ecParams.H);
	}

	public static ECPrivateKeyParameters GetECPrivateKeyParameters(ECDsaCurve curve, byte[] privateKey)
	{
		return new ECPrivateKeyParameters(new BigInteger(1, privateKey), GetECDomainParameters(curve));
	}

	public static ECPublicKeyParameters GetECPublicKeyParameters(ECDsaCurve curve, byte[] publicKey)
	{
		var ecDomainParameters = GetECDomainParameters(curve);

		ECPublicKeyParameters publicKeyParameters;
		if (publicKey.Length == 33)
			publicKeyParameters = new ECPublicKeyParameters(ecDomainParameters.Curve.DecodePoint(publicKey), ecDomainParameters);
		else
			publicKeyParameters = new ECPublicKeyParameters(ecDomainParameters.Curve.CreatePoint(new BigInteger(1, publicKey.Take(publicKey.Length / 2).ToArray()), new BigInteger(1, publicKey.Skip(publicKey.Length / 2).ToArray())), ecDomainParameters);

		return publicKeyParameters;
	}
}


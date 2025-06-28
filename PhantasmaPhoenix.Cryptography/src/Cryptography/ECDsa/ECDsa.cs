using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using PhantasmaPhoenix.Cryptography.Extensions;

namespace PhantasmaPhoenix.Cryptography;

public static class ECDsa
{
	public static byte[] GetPublicKey(byte[] privateKey, bool compressed, ECDsaCurve curve)
	{
		var dom = ECDsaHelpers.GetECDomainParameters(curve);

		var d = new BigInteger(1, privateKey);
		var q = dom.G.Multiply(d);


		var publicParams = new ECPublicKeyParameters(q, dom);
		return publicParams.Q.GetEncoded(compressed);
	}

	public static byte[] CompressPublicKey(byte[] uncompressedPublicKey)
	{
		var x = new BigInteger(1, uncompressedPublicKey.Take(32).ToArray());
		var y = new BigInteger(1, uncompressedPublicKey.Skip(32).ToArray());

		byte prefix = 0x02;
		if (y.Mod(BigInteger.Two) != BigInteger.Zero)
		{
			prefix = 0x03;
		}

		return new byte[] { prefix }.Concat(x.ToByteArrayUnsigned()).ToArray();
	}

	public static byte[] DecompressPublicKey(byte[] compressedPublicKey, ECDsaCurve curve, bool dropUncompressedKeyPrefixByte = false)
	{
		if (compressedPublicKey.Length != 33)
		{
			throw new ArgumentException("Incorrect compressed key length: " + compressedPublicKey.Length);
		}

		var publicKeyParameters = ECDsaHelpers.GetECPublicKeyParameters(curve, compressedPublicKey);

		var uncompressedPublicKey = publicKeyParameters.Q.GetEncoded(false);

		if (dropUncompressedKeyPrefixByte)
		{
			uncompressedPublicKey = uncompressedPublicKey.Skip(1).ToArray();
		}

		return uncompressedPublicKey;
	}

	public static byte[] Sign(byte[] message, byte[] prikey, ECDsaCurve curve)
	{
		var signer = SignerUtilities.GetSigner("SHA256withECDSA");
		var privateKeyParameters = ECDsaHelpers.GetECPrivateKeyParameters(curve, prikey);

		signer.Init(true, privateKeyParameters);
		signer.BlockUpdate(message, 0, message.Length);
		var signature = signer.GenerateSignature();

		return ECDsaHelpers.FromDER(signature);
	}

	private static byte[] ByteArrayLeftPad(byte[] sourceArray, byte padValue, int len)
	{
		if (sourceArray.Length == len)
			return sourceArray;

		var prefixArray = Enumerable.Repeat(padValue, len - sourceArray.Length).ToArray();
		return prefixArray.Concat(sourceArray).ToArray();
	}

	public static byte[] SignDeterministic(byte[] message, byte[] prikey, ECDsaCurve curve)
	{
		var messageHash = message.Sha256();

		var privateKeyParameters = ECDsaHelpers.GetECPrivateKeyParameters(curve, prikey);

		var signer = new ECDsaSigner(new HMacDsaKCalculator(new Sha256Digest()));

		signer.Init(true, privateKeyParameters);

		var RS = signer.GenerateSignature(messageHash);
		var R = ByteArrayLeftPad(RS[0].ToByteArrayUnsigned(), 0, 32);
		var S = ByteArrayLeftPad(RS[1].ToByteArrayUnsigned(), 0, 32);

		return R.Concat(S).ToArray();
	}

	public static bool Verify(byte[] message, byte[] signature, byte[] pubkey, ECDsaCurve curve)
	{
		var signer = SignerUtilities.GetSigner("SHA256withECDSA");
		var publicKeyParameters = ECDsaHelpers.GetECPublicKeyParameters(curve, pubkey);

		signer.Init(false, publicKeyParameters);
		signer.BlockUpdate(message, 0, message.Length);

		return signer.VerifySignature(ECDsaHelpers.ToDER(signature));
	}
}

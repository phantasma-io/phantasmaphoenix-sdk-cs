using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Cryptography.Extensions;
using PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Hex.HexConvertors.Extensions;
using PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Util;

namespace PhantasmaPhoenix.InteropChains.Legacy.Ethereum;

public class EthereumKey : IKeyPair
{
	public byte[] PrivateKey { get; private set; }
	public byte[] PublicKey { get; private set; }
	public readonly string Address;
	public readonly byte[] UncompressedPublicKey;
	public readonly byte[] CompressedPublicKey;

	public EthereumKey(byte[] privateKey)
	{
		if (privateKey.Length != 32 && privateKey.Length != 96 && privateKey.Length != 104)
			throw new ArgumentException();
		this.PrivateKey = new byte[32];
		Buffer.BlockCopy(privateKey, privateKey.Length - 32, PrivateKey, 0, 32);

		this.PublicKey = ECDsa.GetPublicKey(privateKey, true, ECDsaCurve.Secp256k1);
		this.UncompressedPublicKey = ECDsa.GetPublicKey(privateKey, false, ECDsaCurve.Secp256k1).Skip(1).ToArray();
		this.CompressedPublicKey = ECDsa.GetPublicKey(privateKey, true, ECDsaCurve.Secp256k1).ToArray();

		this.Address = PublicKeyToAddress(this.UncompressedPublicKey, ECDsaCurve.Secp256k1);
	}

	public static EthereumKey FromPrivateKey(string prv)
	{
		if (prv == null) throw new ArgumentNullException();
		return new EthereumKey(prv.HexToByteArray());
	}

	public static byte[] FromWIFToBytes(string wif)
	{
		if (wif == null) throw new ArgumentNullException();
		byte[] data = wif.Base58CheckDecode();
		if (data.Length != 34 || data[0] != 0x80 || data[33] != 0x01)
			throw new FormatException();
		byte[] privateKey = new byte[32];
		Buffer.BlockCopy(data, 1, privateKey, 0, privateKey.Length);
		Array.Clear(data, 0, data.Length);
		return privateKey;
	}
	public static EthereumKey FromWIF(string wif)
	{
		return new EthereumKey(FromWIFToBytes(wif));
	}

	private static System.Security.Cryptography.RNGCryptoServiceProvider rnd = new System.Security.Cryptography.RNGCryptoServiceProvider();

	public static EthereumKey Generate()
	{
		var bytes = new byte[32];
		lock (rnd)
		{
			rnd.GetBytes(bytes);
		}
		return new EthereumKey(bytes);
	}

	public string GetWIF()
	{
		byte[] data = new byte[34];
		data[0] = 0x80;
		Buffer.BlockCopy(PrivateKey, 0, data, 1, 32);
		data[33] = 0x01;
		string wif = data.Base58CheckEncode();
		Array.Clear(data, 0, data.Length);
		return wif;
	}

	private static byte[] XOR(byte[] x, byte[] y)
	{
		if (x.Length != y.Length) throw new ArgumentException();
		return x.Zip(y, (a, b) => (byte)(a ^ b)).ToArray();
	}

	public static string PublicKeyToAddress(byte[] publicKey, ECDsaCurve curve)
	{
		if (publicKey.Length == 33)
		{
			publicKey = ECDsa.DecompressPublicKey(publicKey, curve, true);
		}
		var kak = new Sha3Keccack().CalculateHash(publicKey);
		return "0x" + Base16.Encode(kak.Skip(12).ToArray());
	}

	public override string ToString()
	{
		return this.Address;
	}

	public Signature Sign(byte[] msg, Func<byte[], byte[], byte[], byte[]> customSignFunction = null)
	{
		return ECDsaSignature.Generate(this, msg, ECDsaCurve.Secp256k1, customSignFunction);
	}
}


using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Cryptography.Extensions;

namespace PhantasmaPhoenix.InteropChains.Legacy.Neo2;

public class NeoKeys
{
	public readonly byte[] PrivateKey;
	public readonly byte[] PublicKey;
	public readonly byte[] CompressedPublicKey;
	public readonly UInt160 PublicKeyHash;
	public readonly string Address;
	public readonly string AddressN3;
	public readonly string WIF;

	public readonly UInt160 signatureHashN2;
	public readonly UInt160 signatureHashN3;
	public readonly byte[] signatureScriptN2;
	public readonly byte[] signatureScriptN3;

	public NeoKeys(byte[] privateKey)
	{
		if (privateKey.Length != 32 && privateKey.Length != 96 && privateKey.Length != 104)
			throw new ArgumentException();
		this.PrivateKey = new byte[32];

#if NETSTANDARD2_0
		var trimmedPrivateKey = new byte[32];
		Buffer.BlockCopy(privateKey, privateKey.Length - 32, trimmedPrivateKey, 0, 32);
		this.PrivateKey = trimmedPrivateKey;
#else
		privateKey = privateKey[^32..];
		Buffer.BlockCopy(privateKey, privateKey.Length - 32, PrivateKey, 0, 32);
		this.PrivateKey = privateKey[^32..];
#endif

		this.CompressedPublicKey = ECDsa.GetPublicKey(this.PrivateKey, true, ECDsaCurve.Secp256r1);

		this.PublicKeyHash = NeoUtils.ToScriptHash(this.CompressedPublicKey);

		this.signatureScriptN2 = CreateSignatureScript(this.CompressedPublicKey);
		signatureHashN2 = NeoUtils.ToScriptHash(signatureScriptN2);

		this.signatureScriptN3 = CreateSignatureScriptN3(this.CompressedPublicKey);
		signatureHashN3 = NeoUtils.ToScriptHash(signatureScriptN3);

		this.PublicKey = ECDsa.GetPublicKey(this.PrivateKey, false, ECDsaCurve.Secp256r1).Skip(1).ToArray();

		this.Address = NeoUtils.ToAddress(signatureHashN2);
		this.AddressN3 = NeoUtils.ToAddressN3(signatureHashN3);
		this.WIF = GetWIF();
	}

	public static string PublicKeyToN2Address(byte[] publicKey)
	{
		byte[] compressedPublicKey;
		if (publicKey.Length == 33) // Compressed
		{
			compressedPublicKey = publicKey;
		}
		else
		{
			compressedPublicKey = ECDsa.CompressPublicKey(publicKey);
		}

		var signatureScriptN2 = CreateSignatureScript(compressedPublicKey);
		var signatureHashN2 = NeoUtils.ToScriptHash(signatureScriptN2);
		return NeoUtils.ToAddress(signatureHashN2);
	}

	public static NeoKeys FromWIF(string wif)
	{
		if (wif == null) throw new ArgumentNullException();
		byte[] data = wif.Base58CheckDecode();
		if (data.Length != 34 || data[0] != 0x80 || data[33] != 0x01)
			throw new FormatException();
		byte[] privateKey = new byte[32];
		Buffer.BlockCopy(data, 1, privateKey, 0, privateKey.Length);
		Array.Clear(data, 0, data.Length);
		return new NeoKeys(privateKey);
	}

	public static byte[] CreateSignatureScript(byte[] bytes)
	{
		var script = new byte[bytes.Length + 2];

		script[0] = (byte)OpCode.PUSHBYTES33;
		Array.Copy(bytes, 0, script, 1, bytes.Length);
		script[script.Length - 1] = (byte)OpCode.CHECKSIG;

		return script;
	}

	public static byte[] CreateSignatureScriptN3(byte[] bytes)
	{
		var sb = new ScriptBuilder();
		sb.EmitPush(EncodePoint(bytes));
		sb.Emit(OpCode.SYSCALL, BitConverter.GetBytes(666101590));
		var endScript = sb.ToArray();

		return endScript;
	}

	public static byte[] EncodePoint(byte[] bytes)
	{
		byte[] data = new byte[33];
		Array.Copy(bytes, 0, data, 33 - bytes.Length, bytes.Length);
		data[0] = (byte)0x03;
		return data;
	}

	private string GetWIF()
	{
		byte[] data = new byte[34];
		data[0] = 0x80;
		Buffer.BlockCopy(PrivateKey, 0, data, 1, 32);
		data[33] = 0x01;
		string wif = data.Base58CheckEncode();
		Array.Clear(data, 0, data.Length);
		return wif;
	}

	public override string ToString()
	{
		return this.Address;
	}
}

namespace PhantasmaPhoenix.Cryptography.Legacy.Storage;

public static class AES
{
	public static byte[] GenerateIV(int vectorSize)
	{
		var ivBytes = new byte[vectorSize];
		var secRandom = new Org.BouncyCastle.Security.SecureRandom();
		secRandom.NextBytes(ivBytes);

		return ivBytes;
	}

	public static byte[] GCMDecrypt(byte[] data, byte[] key, byte[] iv)
	{
		var keyParamWithIV = new Org.BouncyCastle.Crypto.Parameters.ParametersWithIV(new Org.BouncyCastle.Crypto.Parameters.KeyParameter(key), iv, 0, 16);

		var cipher = Org.BouncyCastle.Security.CipherUtilities.GetCipher("AES/GCM/NoPadding");
		cipher.Init(false, keyParamWithIV);

		return cipher.DoFinal(data);
	}

	public static byte[] GCMEncrypt(byte[] data, byte[] key, byte[] iv)
	{
		var keyParamWithIV = new Org.BouncyCastle.Crypto.Parameters.ParametersWithIV(new Org.BouncyCastle.Crypto.Parameters.KeyParameter(key), iv, 0, 16);

		var cipher = Org.BouncyCastle.Security.CipherUtilities.GetCipher("AES/GCM/NoPadding");
		cipher.Init(true, keyParamWithIV);

		return cipher.DoFinal(data);
	}
}

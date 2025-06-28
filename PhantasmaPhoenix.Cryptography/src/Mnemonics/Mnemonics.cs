using Org.BouncyCastle.Asn1.Sec;
using NBitcoin;

namespace PhantasmaPhoenix.Cryptography;

public static class Mnemonics
{
	public static string GenerateMnemonic(MnemonicPhraseLength mnemonicPhraseLength)
	{
		Mnemonic mnemo = new Mnemonic(Wordlist.English, mnemonicPhraseLength == MnemonicPhraseLength.Twelve_Words ? WordCount.Twelve : WordCount.TwentyFour);
		return mnemo.ToString();
	}

	public static (byte[]?, string?) MnemonicToPK(string mnemonicPhrase, uint pkIndex = 0)
	{
		try
		{
			var mnemonic = new Mnemonic(mnemonicPhrase.ToLowerInvariant());
			var keyPathToDerive = KeyPath.Parse("m/44'/60'/0'/0");
			var pk = mnemonic.DeriveExtKey(null).Derive(keyPathToDerive);
			var keyNew = pk.Derive(pkIndex);
			var pkeyBytes = keyNew.PrivateKey.PubKey.ToBytes();
			var ecParams = SecNamedCurves.GetByName("secp256k1");
			var point = ecParams.Curve.DecodePoint(pkeyBytes);
			var xCoord = point.XCoord.GetEncoded();
			var yCoord = point.YCoord.GetEncoded();
			var uncompressedBytes = new byte[64];
			// copy X coordinate
			Array.Copy(xCoord, uncompressedBytes, xCoord.Length);
			// copy Y coordinate
			for (int i = 0; i < 32 && i < yCoord.Length; i++)
			{
				uncompressedBytes[uncompressedBytes.Length - 1 - i] = yCoord[yCoord.Length - 1 - i];
			}
			return (keyNew.PrivateKey.ToBytes(), null);
		}
		catch (Exception e)
		{
			string? incorrectWord = null;
			var match = System.Text.RegularExpressions.Regex.Match(e.Message, @"Word (\w+) is not in the wordlist for this language");

			if (match.Success)
			{
				incorrectWord = match.Groups[1].Value;
			}

			return (null, incorrectWord);
		}
	}
	public static (string?, string?) MnemonicToWif(string mnemonicPhrase, uint pkIndex = 0)
	{
		var (privKey, incorrectWord) = MnemonicToPK(mnemonicPhrase, pkIndex);

		if (privKey == null)
		{
			return (null, incorrectWord);
		}

		var phaKeys = new PhantasmaKeys(privKey);
		return (phaKeys.ToWIF(), null);
	}
}

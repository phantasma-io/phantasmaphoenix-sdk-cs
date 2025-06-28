namespace PhantasmaPhoenix.Cryptography.Legacy;

public static class MnemonicsLegacy
{
    public static string DecodeLegacySeedToWif(string mnemonicPhrase, string password)
    {
        var privKey = MnemonicToPK(mnemonicPhrase, password);
        var decryptedKeys = new PhantasmaKeys(privKey);
        return decryptedKeys.ToWIF();
    }

    private static byte[] MnemonicToPK(string mnemonicPhrase, string password)
    {
        var bip = new BIP39.BIP39(mnemonicPhrase, password);
        return bip.SeedBytes.Take(32).ToArray();
    }
}

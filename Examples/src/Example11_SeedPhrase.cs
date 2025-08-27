using PhantasmaPhoenix.Cryptography;

// Demonstrates how to generate new seed phrase and how to derive private/public key from it
public static class Example11_SeedPhrase
{
	/// <summary>
	/// Entry point of example. Generates a new seed phrase, derives private key and prints WIF/keys
	/// </summary>
	public static Task Run()
	{
		// Generate a new seed phrase
		var seedPhrase = Mnemonics.GenerateMnemonic(MnemonicPhraseLength.Twelve_Words);

		Console.WriteLine($"New seed phrase: {seedPhrase}");

		// Derive private key from seed phrase
		var (pk, errorMessage) = Mnemonics.MnemonicToPK(seedPhrase);

		if (pk == null || errorMessage != null)
		{
			Console.WriteLine($"Error occured: {errorMessage}");
			return Task.CompletedTask;
		}

		// Create keypair from new private key
		var keys = new PhantasmaKeys(pk);

		// Print address, WIF and keys
		Console.WriteLine($"Address: {keys.Address.Text}");
		Console.WriteLine($"Private (WIF): {keys.ToWIF()}");
		Console.WriteLine($"Private (HEX): {Base16.Encode(keys.PrivateKey)}");
		Console.WriteLine($"Public  (HEX): {Base16.Encode(keys.PublicKey)}");

		return Task.CompletedTask;
	}
}

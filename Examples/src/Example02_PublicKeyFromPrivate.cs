using PhantasmaPhoenix.Cryptography;

// Demonstrates how to load a private key (WIF or HEX) and print its public key and address
public static class Example02_PublicKeyFromPrivate
{
	/// <summary>
	/// Entry point of example. Loads a private key (WIF or HEX) and print its public key and address
	/// </summary>
	/// <param name="wif">Private key in WIF format</param>
	/// <param name="hex">HEX-encoded (Base16) private key raw bytes</param>
	public static Task<PhantasmaKeys> Run(string? wif, string? hex)
	{
		PhantasmaKeys keys;
		if (!string.IsNullOrWhiteSpace(wif))
		{
			keys = PhantasmaKeys.FromWIF(wif);
		}
		else if (!string.IsNullOrWhiteSpace(hex))
		{
			keys = new PhantasmaKeys(Base16.Decode(hex));
		}
		else
		{
			throw new ArgumentException("Empty private key provided");
		}

		Console.WriteLine($"Address: {keys.Address.Text}");
		Console.WriteLine($"Public (HEX): {Base16.Encode(keys.PublicKey)}");

		return Task.FromResult(keys);
	}
}

using PhantasmaPhoenix.Cryptography;

// Demonstrates how to generate a new private/public key pair and print basic info
public static class Example01_GenerateKey
{
	/// <summary>
	/// Entry point of example. Generates a new private/public key pair and prints basic info
	/// </summary>
	public static Task Run()
	{
		// Generate a new random private key and derive address and public key
		var keys = PhantasmaKeys.Generate();

		Console.WriteLine($"Address: {keys.Address.Text}");
		Console.WriteLine($"Private (WIF): {keys.ToWIF()}");
		Console.WriteLine($"Private (HEX): {Base16.Encode(keys.PrivateKey)}");
		Console.WriteLine($"Public  (HEX): {Base16.Encode(keys.PublicKey)}");

		return Task.CompletedTask;
	}
}

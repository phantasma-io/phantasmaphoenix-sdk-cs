using Newtonsoft.Json;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.RPC;

// Demonstrates how to query specific token balance for a given address
public static class Example04_GetAddressTokenBalance
{
	/// <summary>
	/// Entry point of example. Queries specific token balance for a given address
	/// </summary>
	/// <param name="api">Initialized Phantasma API instance used to make RPC calls</param>
	/// <param name="address">Address to check balance for</param>
	/// <param name="symbol">Token symbol to query (e.g. SOUL, KCAL, NFT symbol)</param>
	public static async Task Run(PhantasmaAPI api, string address, string symbol)
	{
		if (string.IsNullOrWhiteSpace(address))
		{
			throw new ArgumentException("Empty address provided");
		}
		if (string.IsNullOrWhiteSpace(symbol))
		{
			throw new ArgumentException("Empty symbol provided");
		}

		// Query information about token by its symbol
		var token = await api.GetTokenAsync(symbol);
		if (token == null)
		{
			throw new Exception("Token not found");
		}

		// Log full token info (decimals, supply, flags, etc.)
		Console.WriteLine($"Token info: {JsonConvert.SerializeObject(token, Formatting.Indented)}");

		// Query token balance for a given address
		var balance = await api.GetTokenBalanceAsync(address, symbol, "main");
		if (balance == null)
		{
			Console.WriteLine("Balance not found");
			return;
		}

		// Check whether the token is fungible (e.g. SOUL, KCAL) or non-fungible (NFT)
		if (token.IsFungible())
		{
			// UnitConversion.ToDecimal() converts raw token amount into human-readable decimal format
			var human = UnitConversion.ToDecimal(balance.Amount, balance.Decimals);
			Console.WriteLine($"Fungible {symbol} amount for {address}: {human}");
		}
		else
		{
			Console.WriteLine($"NFT {symbol} count for {address}: {balance.Amount}");
		}
	}
}

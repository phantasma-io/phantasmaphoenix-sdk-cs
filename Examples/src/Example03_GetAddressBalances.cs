using Newtonsoft.Json;
using PhantasmaPhoenix.RPC;

// Demonstrates how to fetch full account info (including balances) for a given address
public static class Example03_GetAddressBalances
{
	/// <summary>
	/// Entry point of example. Fetches full account info (including balances) for a given address
	/// </summary>
	/// <param name="api">Initialized Phantasma API instance used to make RPC calls</param>
	/// <param name="address">Address to fetch full account info</param>
	public static async Task Run(PhantasmaAPI api, string? address)
	{
		if (string.IsNullOrWhiteSpace(address))
		{
			throw new ArgumentException("Empty address provided");
		}

		// Request account information including all token balances
		var account = await api.GetAccountAsync(address);
		if (account == null)
		{
			Console.WriteLine("Account not found or empty response");
			return;
		}

		// Convert full account result to readable JSON for logging
		var json = JsonConvert.SerializeObject(account, Formatting.Indented);
		Console.WriteLine(json);
	}
}

using Newtonsoft.Json;
using PhantasmaPhoenix.RPC;

// Demonstrates how to fetch an account overview and its balances without pulling an unbounded
// response: getAccountInfo answers name/staking at a cost independent of account size, and the
// fungible balances are walked one bounded page at a time.
public static class Example03_GetAddressBalances
{
	// The node rejects anything outside 1..100, so pages are requested at the documented maximum.
	private const uint PageSize = 100;

	/// <summary>
	/// Entry point of example. Fetches the account overview and lists fungible balances page by page
	/// </summary>
	/// <param name="api">Initialized Phantasma API instance used to make RPC calls</param>
	/// <param name="address">Address to inspect</param>
	public static async Task Run(PhantasmaAPI api, string? address)
	{
		if (string.IsNullOrWhiteSpace(address))
		{
			throw new ArgumentException("Empty address provided");
		}

		// Lightweight overview: registered name and staking only, no id lists.
		var info = await api.GetAccountInfoAsync(address);
		if (info == null)
		{
			Console.WriteLine("Account not found or empty response");
			return;
		}

		Console.WriteLine(JsonConvert.SerializeObject(info, Formatting.Indented));

		// Balances arrive through cursor pagination; an empty/absent cursor marks the last page.
		var cursor = "";
		var page = 0;
		while (true)
		{
			var balances = await api.GetAccountFungibleTokensAsync(address, pageSize: PageSize, cursor: cursor);
			if (balances?.Result == null || balances.Result.Length == 0)
			{
				break;
			}

			Console.WriteLine($"--- fungible balances page {++page} ---");
			Console.WriteLine(JsonConvert.SerializeObject(balances.Result, Formatting.Indented));

			if (string.IsNullOrEmpty(balances.Cursor))
			{
				break;
			}

			cursor = balances.Cursor;
		}
	}
}

using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.RPC;

class Program
{
	// Configure via env vars or just edit constants below
	// PHANTASMA_RPC_URL eg: https://testnet.phantasma.info/rpc
	private static readonly string RpcUrl = Environment.GetEnvironmentVariable("PHANTASMA_RPC_URL")
		?? "https://testnet.phantasma.info/rpc";

	// Target chain Nexus name (e.g. "testnet" or "mainnet")
	private static readonly string Nexus = "testnet";

	// Chain name, don't change, use "main" for all nexuses
	private static readonly string Chain = "main";
	private static PhantasmaKeys? keys = null;

	static async Task Main()
	{
		Console.WriteLine("Phantasma Phoenix's Examples for C# SDK");
		Console.WriteLine($"RPC: {RpcUrl} | Nexus: {Nexus}");

		using var rpcClient = new RpcClient(); // Optionally pass HttpClient/ILogger
		using var api = new PhantasmaAPI(RpcUrl, rpcClient);

		while (true)
		{
			PrintMenu();
			Console.Write("> ");
			var input = Console.ReadLine();
			if (!int.TryParse(input, out var choice))
			{
				Console.WriteLine("Invalid number");
				continue;
			}
			if (choice == 0) { Console.WriteLine("Bye"); break; }

			try
			{
				switch (choice)
				{
					case 1: await Example01_GenerateKey.Run(); break;
					case 2:
						{
							await InitPrivateKey();
							break;
						}
					case 3:
						{
							var address = EnvOrAsk("TEST_ADDRESS", "Enter address (P...): ");
							await Example03_GetAddressBalances.Run(api, address);
							break;
						}
					case 4:
						{
							var address = EnvOrAsk("TEST_ADDRESS", "Enter address (P...): ");
							var symbol = EnvOrAsk("TOKEN_SYMBOL", "Enter token symbol (e.g. SOUL): ");
							await Example04_GetAddressTokenBalance.Run(api, address, symbol);
							break;
						}
					case 5:
						{
							await InitPrivateKey();
							var to = EnvOrAsk("DEST_ADDRESS", "Enter destination address (P...): ");
							var symbol = EnvOrAsk("TOKEN_SYMBOL", "Enter token symbol (e.g. SOUL): ");
							var amountStr = EnvOrAsk("TOKEN_AMOUNT", "Enter amount: ");
							if (!decimal.TryParse(amountStr, out var amount)) throw new Exception("Invalid amount");
							await Example05_SendToken.Run(api, keys, to, Nexus, Chain, symbol, amount);
							break;
						}
					case 6:
						{
							var tx = EnvOrAsk("TX_HASH", "Enter tx hash: ");
							await Example06_CheckTransactionState.Run(api, tx, null);
							break;
						}
					case 7:
						{
							await InitPrivateKey();
							var amountStr = EnvOrAsk("TOKEN_AMOUNT", "Enter SOUL amount to stake: ");
							if (!decimal.TryParse(amountStr, out var amount)) throw new Exception("Invalid amount");
							await Example07_StakeSoul.Run(api, keys, Nexus, Chain, amount);
							break;
						}
					case 8:
						{
							await InitPrivateKey();
							var amountStr = EnvOrAsk("TOKEN_AMOUNT", "Enter SOUL amount to unstake: ");
							if (!decimal.TryParse(amountStr, out var amount)) throw new Exception("Invalid amount");
							await Example08_UnstakeSoul.Run(api, keys, Nexus, Chain, amount);
							break;
						}
					case 9:
						{
							await InitPrivateKey();
							await Example09_ClaimKcal.Run(api, keys, Nexus, Chain);
							break;
						}
					case 10:
						{
							var address = EnvOrAsk("TEST_ADDRESS", "Enter address to monitor (P...): ");
							using var cts = new CancellationTokenSource();
							Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
							await Example10_WaitIncomingTx_ReadBlocks.Run(api, address, Chain, cts.Token);
							break;
						}
					case 11: await Example11_SeedPhrase.Run(); break;
					default:
						Console.WriteLine("Unknown choice");
						break;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error: {ex.Message}");
			}

			Console.WriteLine();
		}
	}

	private static string EnvOrAsk(string env, string prompt, string? fallback = null)
	{
		var v = Environment.GetEnvironmentVariable(env);
		if (!string.IsNullOrWhiteSpace(v)) return v;
		Console.Write(prompt);
		v = Console.ReadLine();
		if (string.IsNullOrWhiteSpace(v)) v = fallback ?? "";
		return v;
	}

	private static async Task InitPrivateKey()
	{
		if (keys != null)
		{
			// Already initialized
			return;
		}

		// Ask for private key in WIF format
		var wif = EnvOrAsk("EXAMPLE_WIF", "Enter WIF (or skip to HEX): ");

		// Ask for HEX-encoded private key only if WIF was not provided
		var hex = !string.IsNullOrEmpty(wif) ? null : EnvOrAsk("EXAMPLE_HEX", "Enter HEX: ");

		keys = await Example02_PublicKeyFromPrivate.Run(wif, hex);
	}

	private static void PrintMenu()
	{
		Console.WriteLine();
		Console.WriteLine("Choose example:");
		Console.WriteLine("  1  = GenerateKey");
		Console.WriteLine("  2  = PublicKeyFromPrivate");
		Console.WriteLine("  3  = GetAddressBalances");
		Console.WriteLine("  4  = GetAddressTokenBalance");
		Console.WriteLine("  5  = SendToken");
		Console.WriteLine("  6  = CheckTransactionState");
		Console.WriteLine("  7  = StakeSoul");
		Console.WriteLine("  8  = UnstakeSoul");
		Console.WriteLine("  9  = ClaimKcal");
		Console.WriteLine("  10 = WaitIncomingTx (scan blocks)");
		Console.WriteLine("  11  = SeedPhrase");
		Console.WriteLine("  0  = Exit");
	}
}

using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.RPC;
using PhantasmaPhoenix.VM;

// Demonstrates how to transfer fungible tokens
public static class Example05_SendToken
{
	/// <summary>
	/// Entry point of example. Transfers fungible tokens
	/// </summary>
	/// <param name="api">Initialized Phantasma API instance used to make RPC calls</param>
	/// <param name="keys">Private key of transaction sender</param>
	/// <param name="destination">Recipient of tokens</param>
	/// <param name="nexus">Chain nexus</param>
	/// <param name="chain">Chain name (always "main")</param>
	/// <param name="symbol">Token symbol to transfer (fungibles only in this example)</param>
	/// <param name="amount">Amount of tokens to transfer</param>
	public static async Task Run(PhantasmaAPI api, PhantasmaKeys? keys, string destination, string nexus, string chain, string symbol, decimal amount)
	{
		if (keys == null)
		{
			throw new ArgumentException("Private key not provided");
		}
		if (string.IsNullOrWhiteSpace(destination))
		{
			throw new ArgumentException("Empty destination address provided");
		}

		// Get transaction sender's address from private key
		var senderAddress = keys.Address;

		var token = await api.GetTokenAsync(symbol) ?? throw new Exception("Token not found");
		if (!token.IsFungible())
		{
			throw new Exception("Token is not fungible");
		}

		// TODO: Adapt to new fee model
		// Use these values for now
		var feePrice = 100000;
		var feeLimit = 21000;

		byte[] script;
		try
		{
			// ScriptBuilder is used to create a serialized transaction script
			var sb = new ScriptBuilder();

			// Instruction to allow gas fees for the transaction - required by all transaction scripts
			sb.AllowGas(senderAddress, Address.Null, feePrice, feeLimit);

			// Add instruction to transfer tokens from sender to destination, converting human-readable amount to chain format
			sb.TransferTokens(symbol, senderAddress, Address.Parse(destination),
				UnitConversion.ToBigInteger(amount, token.Decimals));

			// Spend gas necessary for transaction execution
			sb.SpendGas(senderAddress);

			// Finalize and get raw bytecode for the transaction script
			script = sb.EndScript();
		}
		catch (Exception e)
		{
			throw new Exception($"Could not build transaction script: {e.Message}");
		}

        // Signing transaction with private key and sending it to the chain
		var txHash = await api.SignAndSendTransactionAsync(keys, nexus, script, chain, "example5-tx-payload");
		if (!string.IsNullOrEmpty(txHash))
		{
			Console.WriteLine($"Transaction was sent, hash: {txHash}");

			// Start polling to track transaction execution status on-chain
			await Example06_CheckTransactionState.Run(api, txHash,
				state => Console.WriteLine($"Tx completed with state: {state?.ToString() ?? "Unknown"}"));
		}
		else
		{
			throw new Exception("Failed to send transaction");
		}
	}
}

using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.RPC;
using PhantasmaPhoenix.VM;

// Demonstrates how to claim all KCAL tokens earned from SOUL staking
public static class Example09_ClaimKcal
{
	/// <summary>
	/// Entry point of example. Claims all KCAL tokens earned from SOUL staking
	/// </summary>
	/// <param name="api">Initialized Phantasma API instance used to make RPC calls</param>
	/// <param name="keys">Private key of transaction sender</param>
	/// <param name="nexus">Chain nexus</param>
	/// <param name="chain">Chain name (always "main")</param>
	public static async Task Run(PhantasmaAPI api, PhantasmaKeys? keys, string nexus, string chain)
	{
		if (keys == null)
		{
			throw new ArgumentException("Private key not provided");
		}

		// Get transaction sender's address from private key
		var senderAddress = keys.Address;

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

			// Add instruction to claim earned KCAL tokens
			sb.CallContract("stake", "Claim", senderAddress, senderAddress);

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
		var hashText = await api.SignAndSendTransactionAsync(keys, nexus, script, chain, "example9-tx-payload");
		if (!string.IsNullOrEmpty(hashText))
		{
			Console.WriteLine($"Transaction was sent, hash: {hashText}");

			// Start polling to track transaction execution status on-chain
			await Example06_CheckTransactionState.Run(api, hashText,
				state => Console.WriteLine($"Tx completed with state: {state?.ToString() ?? "Unknown"}"));
		}
		else
		{
			throw new Exception("Empty transaction hash returned");
		}
	}
}

using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.RPC;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.VM;
using PhantasmaPhoenix.Core;

// Demonstrates how to unstake SOUL tokens
public static class Example08_UnstakeSoul
{
	/// <summary>
	/// Entry point of example. Unstakes SOUL tokens
	/// </summary>
	/// <param name="api">Initialized Phantasma API instance used to make RPC calls</param>
	/// <param name="keys">Private key of transaction sender</param>
	/// <param name="nexus">Chain nexus</param>
	/// <param name="chain">Chain name (always "main")</param>
	/// <param name="amount">Amount of tokens to stake</param>
	public static async Task Run(PhantasmaAPI api, PhantasmaKeys? keys, string nexus, string chain, decimal amountSoul)
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

			// Add instruction to unstake tokens, converting human-readable amount to chain format
			sb.CallContract("stake", "Unstake", senderAddress,
				UnitConversion.ToBigInteger(amountSoul, DomainSettings.StakingTokenDecimals));

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
		var hashText = await api.SignAndSendTransactionAsync(keys, nexus, script, chain, "example8-tx-payload");
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

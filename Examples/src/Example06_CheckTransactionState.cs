using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.RPC;

// Demonstrates how to check the status of a transaction until completion
public static class Example06_CheckTransactionState
{
	/// <summary>
	/// Entry point of example. Checks the status of a transaction until completion
	/// </summary>
	/// <param name="api">Initialized Phantasma API instance used to make RPC calls</param>
	/// <param name="txHash">Hash of transaction to check</param>
	/// <param name="onDone">Callback called once status of transaction is determined or upon a timeout. Can be null, optional. If state could not be determined (timeout), callback(null) is invoked</param>
	public static async Task Run(PhantasmaAPI api, string txHash, Action<ExecutionState?>? onDone)
	{
		if (string.IsNullOrWhiteSpace(txHash))
		{
			throw new ArgumentException("Empty tx hash provided");
		}

		// Flag to stop polling loop once the transaction is finalized
		bool done = false;

		// Counter for how many times we've polled for transaction status
		uint txStatusQueryAttempts = 0;

		// Counter for how many times we've attempted to get failure debug details, if needed
		uint failureDetailsQueryAttempts = 0;

		while (!done)
		{
			try
			{
				// Make RPC call to fetch transaction info from the chain
				var tx = await api.GetTransactionAsync(txHash);
				if (tx == null)
				{
					Console.WriteLine("Transaction not found");
				}
				else
				{
					// Log the current execution state: Running, Halt (success), or other (failure)
					Console.WriteLine($"Transaction state is: {tx.State}");

					switch (tx.State)
					{
						case ExecutionState.Running:
							// Transaction is still being processed by the chain
							Console.WriteLine("Transaction is still processing...");
							break;

						case ExecutionState.Halt:
							// Transaction completed successfully (execution halted without errors)

							// Check if any result string is available (may be empty if not applicable)
							if (string.IsNullOrEmpty(tx.Result))
								Console.WriteLine("Transaction executed successfully, no result available");
							else
								Console.WriteLine($"Transaction executed successfully with result '{tx.Result}'");
							done = true;

							// Notify success with result value and no error info
							onDone?.Invoke(tx.State);
							continue;

						default:
							// Transaction failed. We check if we have additional details about failure available.
							// If failure details are not yet available, and we haven't tried too many times - wait and retry
							if (tx.DebugComment == null && failureDetailsQueryAttempts < 6)
							{
								// Inform user that we're retrying in case debug info hasn't yet been indexed on the node
								Console.WriteLine($"Waiting for failure details... Attempt {failureDetailsQueryAttempts + 1}/6");
								failureDetailsQueryAttempts++;
							}
							else
							{
								// Final failure state reached, log failure details and return via callback
								Console.WriteLine($"Transaction failed with state: {tx.State}. Result: {tx.Result}. Details: {tx.DebugComment}");
								done = true;

								// Notify failure with state, raw result, and debug comment if available
								onDone?.Invoke(tx.State);
							}
							break;
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error querying tx: {ex.Message}");
			}

			// If still running (or DebugComment is unavailable), wait 1 second before checking again
			if (!done)
			{
				// Stop retrying if status check exceeded max allowed attempts
				if (txStatusQueryAttempts == 30)
				{
					Console.WriteLine($"Query attempts exhausted after {txStatusQueryAttempts} attempts, tx state could not be confirmed");

					// Notify that the transaction status could not be confirmed at all (timeout case)
					onDone?.Invoke(null);
					break;
				}
				await Task.Delay(1000);
				txStatusQueryAttempts++;
			}
		}
	}
}

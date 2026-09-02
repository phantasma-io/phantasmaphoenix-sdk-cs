using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using PhantasmaPhoenix.RPC;

// Demonstrates the Carbon fee flow under gas model v2: build a native KCAL transfer, plan its fee
// from the message against the chain's own prices, and show the plan in KCAL and SOUL. Nothing is
// signed or sent - the one-step send is `api.SendTransactionAsync(transfer, keys)`.
public static class Example12_CarbonTransferFeePlan
{
	/// <summary>
	/// Entry point of example. Plans a 1 KCAL native transfer and prints the plan.
	/// </summary>
	/// <param name="api">Initialized Phantasma API instance used to make RPC calls</param>
	/// <param name="keys">The sender's keys (only the public key is used: the message is planned, not signed)</param>
	/// <param name="destinationPublicKeyHex">The recipient's 32-byte public key, hex encoded</param>
	public static async Task Run(PhantasmaAPI api, PhantasmaKeys keys, string destinationPublicKeyHex)
	{
		var destination = destinationPublicKeyHex.FromHex();
		if (destination == null || destination.Length != 32)
		{
			throw new ArgumentException("Destination must be a 32-byte public key in hex");
		}

		// 1. Build: the message only. Builders carry no prices; maxGas stays 0 until the fee is planned.
		var transfer = NativeTxHelper.TransferFungible(new TransferFungibleParams
		{
			From = new Bytes32(keys.PublicKey),
			To = new Bytes32(destination),
			TokenId = 1, // KCAL
			Amount = 10_000_000_000, // 1 KCAL in atoms (KCAL has ten decimals)
		});
		Console.WriteLine($"Built a {transfer.type} message; maxGas before planning: {transfer.maxGas}");

		// 2. Plan: the gas bill and storage deposit for this message, from the chain's own prices. A
		//    KCAL transfer escrows nothing - the gas token's balance rows are free - so the storage
		//    ceiling comes out as 0.
		var plan = await api.Fees.PlanAsync(transfer);
		var summary = FeePlanSummary.Summarize(plan);
		Console.WriteLine($"Kind: {plan.Kind}, signed size: {plan.EnvelopeBytes} bytes");
		Console.WriteLine($"Gas bill: {summary.GasBill} KCAL, offer: {summary.GasOffer} KCAL, storage ceiling: {summary.StorageCeiling} SOUL");

		// 3-4. Sign and send would be `await api.SendTransactionAsync(transfer, keys)`, which also plans
		//      an unplanned message. This example stops here on purpose.
		var chainParams = await api.Fees.ChainParamsAsync();
		Console.WriteLine($"Chain gas model: {chainParams.GasModelVersion}, expiry window: {chainParams.ExpiryWindow} ms");
	}
}

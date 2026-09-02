using System;
using System.Globalization;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

/// <summary>
/// Response of the estimateTransaction RPC method: the exact fee bill of one serialized
/// transaction envelope, computed by dry-running it against current chain state (gas-model-v2
/// Tier-2). 64-bit amounts are serialized as decimal strings (they can exceed the 2^53 precision
/// of JSON numbers in JavaScript parsers). Amounts are kcal-base atoms of the gas token; escrow
/// amounts are data-token atoms. Service availability (routing, gas model, node budget) is
/// reported as a standard RPC error, never through this shape.
/// </summary>
public class EstimateTransactionResult
{
	[ApiDescription("True when the transaction would not complete on-chain as submitted; see AbortReason")]
	public bool WouldAbort { get; set; }

	[ApiDescription("Rejection or abort reason when WouldAbort is true; empty otherwise")]
	public string? AbortReason { get; set; }

	[ApiDescription("Settled gas bill in kcal-base, including the minimum-bill floor and the maxGas clamp (aborted transactions still pay)")]
	public string? GasBillKcalBase { get; set; }

	[ApiDescription("Newly paid storage quanta the transaction creates (dataRows * dataEscrowPerRow == dataEscrowAtoms)")]
	public string? DataRows { get; set; }

	[ApiDescription("Gross storage escrow paid for grown rows, in data-token atoms")]
	public string? DataEscrowAtoms { get; set; }

	[ApiDescription("Gross storage refunds for shrunk rows, in data-token atoms")]
	public string? DataRefundAtoms { get; set; }

	[ApiDescription("Recommended TxMsg.maxGas: the bill plus a 15% state-drift margin, floored at the chain minimums; 0 when WouldAbort")]
	public string? RecommendedMaxGas { get; set; }

	[ApiDescription("Recommended TxMsg.maxData: the net escrow plus a 15% margin, aligned up to whole rows; 0 when WouldAbort or when nothing is escrowed")]
	public string? RecommendedMaxData { get; set; }

	public EstimateTransactionResult() { }

	/// <summary>
	/// Converts a completed estimate into the same <see cref="FeeQuote"/> that Tier-1 estimates
	/// produce. Wallet code then reads both tiers the same way. MaxGas and MaxData are the
	/// recommended ceilings, and ExpectedGasBill is the exact settled bill. Throws when WouldAbort is
	/// set, because an aborted simulation has no recommendations. Retry with a higher offer, or fall
	/// back to Tier-1.
	/// </summary>
	public FeeQuote ToFeeQuote()
	{
		if (WouldAbort)
			throw new InvalidOperationException($"estimateTransaction reported the transaction would abort: {AbortReason}");
		return new FeeQuote(
			ParseU64(RecommendedMaxGas, nameof(RecommendedMaxGas)),
			ParseU64(RecommendedMaxData, nameof(RecommendedMaxData)),
			ParseU64(GasBillKcalBase, nameof(GasBillKcalBase)));
	}

	private static ulong ParseU64(string? value, string fieldName)
	{
		if (string.IsNullOrEmpty(value))
			throw new FormatException($"estimateTransaction field {fieldName} is missing or empty");
		return ulong.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
	}
}

using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class BlockResult
{
	public string Hash { get; set; } = "";

	[ApiDescription("Hash of previous block")]
	public string PreviousHash { get; set; } = "";

	public uint Timestamp { get; set; }

	public uint Height { get; set; }

	[ApiDescription("Address of chain where the block belongs")]
	public string ChainAddress { get; set; } = "";

	[ApiDescription("Network protocol version")]
	public uint Protocol { get; set; }

	[ApiDescription("List of transactions in block")]
	public TransactionResult[] Txs { get; set; } = Array.Empty<TransactionResult>();

	[ApiDescription("Address of validator who minted the block")]
	public string ValidatorAddress { get; set; } = "";

	// Distinct from ValidatorAddress: ValidatorAddress comes from the consensus log wrapper (who
	// committed the block), while this is the payout address stamped inside the hashed block input.
	// Today producers stamp their own identity, so the two usually match; a configurable payout
	// address is a planned compatible extension, so consumers must not assume equality.
	[ApiDescription("Fee payout address stamped by the block producer inside the block input; present on gas-model-v2 blocks only, null on earlier blocks")]
	public string? ProducerAddress { get; set; }

	[ApiDescription("Amount of KCAL rewarded by this fees in this block")]
	public string Reward { get; set; } = "";

	[ApiDescription("Block events")]
	public EventResult[]? Events { get; set; }

	[ApiDescription("Block oracles")]
	public OracleResult[]? Oracles { get; set; }

	public BlockResult() { }
}

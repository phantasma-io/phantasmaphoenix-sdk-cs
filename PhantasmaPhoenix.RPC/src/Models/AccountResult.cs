using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class AccountResult
{
	public string Address { get; set; } = "";
	public string Name { get; set; } = "";

	[ApiDescription("Info about staking if available")]
	public StakeResult Stakes { get; set; } = new();

	// Flat scalars kept for wire compatibility; superseded by the Stakes object above, which carries
	// the same amount plus the stake time. Not marked [Obsolete]: the node assigns both fields when
	// building this response, so attributing them would turn its own loader into a warning source.
	public string Stake { get; set; } = "";
	public string Unclaimed { get; set; } = "";

	[ApiDescription("Amount of available KCAL for relay channel")]
	public string? Relay { get; set; }

	[ApiDescription("Validator role")]
	public string Validator { get; set; } = "";

	[ApiDescription("Info about storage if available")]
	public StorageResult Storage { get; set; } = new();

	public BalanceResult[] Balances { get; set; } = Array.Empty<BalanceResult>();

	public string[]? Txs { get; set; }

	public AccountResult() { }
}

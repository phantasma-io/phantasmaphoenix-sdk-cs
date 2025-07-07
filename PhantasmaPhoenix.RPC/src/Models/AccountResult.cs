using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class AccountResult
{
	public string Address { get; set; }
	public string Name { get; set; }

	[ApiDescription("Info about staking if available")]
	public StakeResult Stakes { get; set; }

	public string Stake { get; set; } //Deprecated
	public string Unclaimed { get; set; } //Deprecated

	[ApiDescription("Amount of available KCAL for relay channel")]
	public string Relay { get; set; }

	[ApiDescription("Validator role")]
	public string Validator { get; set; }

	[ApiDescription("Info about storage if available")]
	public StorageResult Storage { get; set; }

	public BalanceResult[] Balances { get; set; }

	[Obsolete("The txs property is deprecated and will be removed in future versions.")]
	public string[] Txs { get; set; }

	public AccountResult() { }
}


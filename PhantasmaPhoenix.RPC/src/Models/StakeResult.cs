using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class StakeResult
{
	[ApiDescription("Amount of staked SOUL")]
	public string Amount { get; set; }

	[ApiDescription("Time of last stake")]
	public uint Time { get; set; }

	[ApiDescription("Amount of claimable KCAL")]
	public string Unclaimed { get; set; }

	public StakeResult() { }
}

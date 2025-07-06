namespace PhantasmaPhoenix.RPC.Models;

public class LeaderboardResult
{
	public string Name { get; set; }
	public LeaderboardRowResult[] Rows { get; set; }
}

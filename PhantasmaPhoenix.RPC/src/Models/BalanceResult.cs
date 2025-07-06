namespace PhantasmaPhoenix.RPC.Models;

public class BalanceResult
{
	public string Chain { get; set; }
	public string Amount { get; set; }
	public string Symbol { get; set; }
	public uint Decimals { get; set; }
	public string[] Ids { get; set; }
}

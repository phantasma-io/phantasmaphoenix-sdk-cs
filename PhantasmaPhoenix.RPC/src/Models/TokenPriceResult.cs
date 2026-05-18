namespace PhantasmaPhoenix.RPC.Models;

public class TokenPriceResult
{
	public uint Timestamp { get; set; }
	public string Open { get; set; } = string.Empty;
	public string High { get; set; } = string.Empty;
	public string Low { get; set; } = string.Empty;
	public string Close { get; set; } = string.Empty;

	public TokenPriceResult() { }
}

using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class TokenResult
{
	[ApiDescription("Ticker symbol for the token")]
	public string Symbol { get; set; }

	public string Name { get; set; }

	[ApiDescription("Amount of decimals when converting from fixed point format to decimal format")]
	public uint Decimals { get; set; }

	[ApiDescription("Amount of minted tokens")]
	public string CurrentSupply { get; set; }

	[ApiDescription("Max amount of tokens that can be minted")]
	public string MaxSupply { get; set; }

	[ApiDescription("Total amount of burned tokens")]
	public string BurnedSupply { get; set; }

	[ApiDescription("Address of token contract")]
	public string Address { get; set; }

	[ApiDescription("Owner address")]
	public string Owner { get; set; }

	public string Flags { get; set; }

	[ApiDescription("Script attached to token, in hex")]
	public string Script { get; set; }

	[ApiDescription("Series info. NFT only")]
	public TokenSeriesResult[] Series { get; set; }

	// TODO Commented: TokenExternalResult[], TokenPriceResult[], should we still implement it somehow?
	// [ApiDescription("External platforms info")]
	// public TokenExternalResult[] external { get; set; }

	// [ApiDescription("Cosmic swap historic data")]
	// public TokenPriceResult[] price { get; set; }

	public TokenResult() { }

	public bool IsBurnable()
	{
		return Flags.Contains(TokenFlags.Burnable.ToString());
	}
	public bool IsFungible()
	{
		return Flags.Contains(TokenFlags.Fungible.ToString());
	}
	public bool IsTransferable()
	{
		return Flags.Contains(TokenFlags.Transferable.ToString());
	}
}

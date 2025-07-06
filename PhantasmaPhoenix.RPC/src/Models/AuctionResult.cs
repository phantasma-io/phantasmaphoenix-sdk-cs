using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class AuctionResult
{
	[ApiDescription("Address of auction creator")]
	public string CreatorAddress { get; set; }

	[ApiDescription("Address of auction chain")]
	public string ChainAddress { get; set; }

	public uint StartDate { get; set; }
	public uint EndDate { get; set; }
	public string BaseSymbol { get; set; }
	public string QuoteSymbol { get; set; }
	public string TokenId { get; set; }
	public string Price { get; set; }
	public string EndPrice { get; set; }
	public string ExtensionPeriod { get; set; }
	public string Type { get; set; }
	public string Rom { get; set; }
	public string Ram { get; set; }
	public string ListingFee { get; set; }
	public string CurrentWinner { get; set; }
}

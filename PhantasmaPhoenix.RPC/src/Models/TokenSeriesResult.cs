using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class TokenSeriesResult
{
	public uint SeriesId { get; set; }

	[ApiDescription("Current amount of tokens in circulation")]
	public string CurrentSupply { get; set; }

	[ApiDescription("Maximum possible amount of tokens")]
	public string MaxSupply { get; set; }

	[ApiDescription("Total amount of burned tokens")]
	public string BurnedSupply { get; set; }

	public string Mode { get; set; }

	public string Script { get; set; }

	[ApiDescription("List of methods")]
	public ABIMethodResult[] Methods { get; set; }

	public TokenSeriesResult() { }
}

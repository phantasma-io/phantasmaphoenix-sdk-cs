using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class TokenSeriesResult
{
	[ApiDescription("Phantasma series ID, up to 32 bytes")]
	public string SeriesId { get; set; }

	[ApiDescription("Carbon token ID to which this series belongs")]
	public ulong carbonTokenId { get; set; }

	[ApiDescription("Carbon series ID")]
	public uint carbonSeriesId { get; set; }

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

	[ApiDescription("Series metadata")]
	public TokenPropertyResult[] Metadata { get; set; }

	public TokenSeriesResult() { }
}

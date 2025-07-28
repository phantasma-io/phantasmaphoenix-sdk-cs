using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class TokenDataResult
{
	[ApiDescription("id of token")]
	public string Id { get; set; }

	[ApiDescription("series id of token")]
	public string Series { get; set; }

	[ApiDescription("mint number of token")]
	public string Mint { get; set; }

	[ApiDescription("Chain where currently is stored")]
	public string ChainName { get; set; }

	[ApiDescription("Address who currently owns the token")]
	public string OwnerAddress { get; set; }

	[ApiDescription("Address who minted the token")]
	public string CreatorAddress { get; set; }

	[ApiDescription("Writable data of token, hex encoded")]
	public string Ram { get; set; }

	[ApiDescription("Read-only data of token, hex encoded")]
	public string Rom { get; set; }

	[ApiDescription("Status of nft")]
	public TokenStatus Status { get; set; }

	public TokenPropertyResult[] Infusion { get; set; }

	public TokenPropertyResult[] Properties { get; set; }

	public TokenDataResult() { }
}

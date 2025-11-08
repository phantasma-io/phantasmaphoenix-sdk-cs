using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class TokenDataResult
{
	[ApiDescription("Phantasma NFT ID")]
	public string Id { get; set; }

	[ApiDescription("Phantasma series ID to which this NFT belongs")]
	public string Series { get; set; }

	[ApiDescription("Carbon token ID to which this NFT belongs")]
	public string carbonTokenId { get; set; }

	[ApiDescription("Carbon NFT address (hex encoded, 32 bytes)")]
	public string carbonNftAddress { get; set; }

	[ApiDescription("NFT mint number")]
	public string Mint { get; set; }

	[ApiDescription("NFT's chain")]
	public string ChainName { get; set; }

	[ApiDescription("Address who currently owns the token")]
	public string OwnerAddress { get; set; }

	[ApiDescription("Address who minted the token")]
	public string CreatorAddress { get; set; }

	[ApiDescription("Writable data of token, hex encoded")]
	public string Ram { get; set; }

	[ApiDescription("Read-only data of token, hex encoded")]
	public string Rom { get; set; }

	[ApiDescription("Status of NFT")]
	public TokenStatus Status { get; set; }

	public TokenPropertyResult[] Infusion { get; set; }

	public TokenPropertyResult[] Properties { get; set; }

	public TokenDataResult() { }
}

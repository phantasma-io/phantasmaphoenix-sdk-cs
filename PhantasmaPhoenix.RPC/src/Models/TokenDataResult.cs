using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.RPC.Annotations;
using Newtonsoft.Json;

namespace PhantasmaPhoenix.RPC.Models;

[JsonConverter(typeof(TokenDataResultJsonConverter))]
public class TokenDataResult
{
	[ApiDescription("Phantasma NFT ID")]
	public string Id { get; set; } = string.Empty;

	[ApiDescription("Phantasma series ID to which this NFT belongs")]
	public string Series { get; set; } = string.Empty;

	[ApiDescription("Carbon token ID to which this NFT belongs")]
	public string carbonTokenId { get; set; } = string.Empty;

	[ApiDescription("Carbon series ID to which this NFT belongs")]
	public string carbonSeriesId { get; set; } = string.Empty;

	[ApiDescription("Carbon NFT address (hex encoded, 32 bytes)")]
	public string carbonNftAddress { get; set; } = string.Empty;

	[ApiDescription("NFT mint number")]
	public string Mint { get; set; } = string.Empty;

	[ApiDescription("NFT's chain")]
	public string ChainName { get; set; } = string.Empty;

	[ApiDescription("Address who currently owns the token")]
	public string OwnerAddress { get; set; } = string.Empty;

	[ApiDescription("Address who minted the token")]
	public string CreatorAddress { get; set; } = string.Empty;

	[ApiDescription("Writable data of token, hex encoded")]
	public string Ram { get; set; } = string.Empty;

	[ApiDescription("Read-only data of token, hex encoded")]
	public string Rom { get; set; } = string.Empty;

	[ApiDescription("Status of NFT")]
	public TokenStatus Status { get; set; }

	public TokenPropertyResult[] Infusion { get; set; } = Array.Empty<TokenPropertyResult>();

	public TokenPropertyResult[] Properties { get; set; } = Array.Empty<TokenPropertyResult>();

	public TokenDataResult() { }
}

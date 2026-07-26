using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

// Lightweight account overview returned by getAccountInfo: the replacement for the parts of
// getAccount that a wallet needs on every refresh. It deliberately carries no balance or NFT id
// lists - those are served by the cursor-paginated getAccountFungibleTokens / getAccountNFTs /
// getAccountOwnedTokens endpoints - so the cost of this call does not grow with how much an address
// holds. Keeping the model minimal is a compatibility commitment: fields can be added later, never
// cheaply removed.
public class AccountInfoResult
{
	public string Address { get; set; } = "";

	[ApiDescription("On-chain registered name, or \"anonymous\" when the address has none")]
	public string Name { get; set; } = "";

	// Wire name differs from AccountResult, which exposes the same object under "stakes"; both are
	// fixed by their respective endpoints, so neither can be renamed for symmetry.
	[ApiDescription("Staking info: staked SOUL amount, last stake time, claimable KCAL")]
	public StakeResult Stake { get; set; } = new();

	public AccountInfoResult() { }
}

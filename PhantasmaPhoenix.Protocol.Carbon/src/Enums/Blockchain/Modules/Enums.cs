namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;

public enum ModuleId : uint
{
	Internal = 0xFFFFFFFFu, // C++'s ~0U
	Governance = 0u,
	Token = 1u,
	PhantasmaVm = 2u,
	Organization = 3u,
	Market = 4u,
}

public enum TokenFlags
{
	None = 0,
	BigFungible = 1 << 0,
	NonFungible = 1 << 1,
};

public enum TokenContract_Methods
{
	TransferFungible = 0,
	TransferNonFungible = 1,
	CreateToken = 2,
	MintFungible = 3,
	BurnFungible = 4,
	GetBalance = 5,
	CreateTokenSeries = 6,
	DeleteTokenSeries = 7,
	MintNonFungible = 8,
	BurnNonFungible = 9,
	GetInstances = 10,
	GetNonFungibleInfo = 11,
	GetNonFungibleInfoByRomId = 12,
	GetSeriesInfo = 13,
	GetSeriesInfoByMetaId = 14,
	GetTokenInfo = 15,
	GetTokenInfoBySymbol = 16,
	GetTokenSupply = 17,
	GetSeriesSupply = 18,
	GetTokenIdBySymbol = 19,
	GetBalances = 20,
	CreateMintedTokenSeries = 21,
}

public enum MarketContract_Methods
{
	SellToken = 0,
	SellTokenById = 1,
	CancelSale = 2,
	CancelSaleById = 3,
	BuyToken = 4,
	BuyTokenById = 5,
	GetTokenListingCount = 6,
	GetTokenListingInfo = 7,
	GetTokenListingInfoById = 8,
}

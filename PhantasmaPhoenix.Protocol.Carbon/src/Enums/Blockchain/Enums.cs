namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public enum TxTypes
{
	Call = 0,
	Call_Multi = 1,
	Trade = 2,
	TransferFungible = 3,
	TransferFungible_GasPayer = 4,
	TransferNonFungible_Single = 5,
	TransferNonFungible_Single_GasPayer = 6,
	TransferNonFungible_Multi = 7,
	TransferNonFungible_Multi_GasPayer = 8,
	MintFungible = 9,
	BurnFungible = 10,
	BurnFungible_GasPayer = 11,
	MintNonFungible = 12,
	BurnNonFungible = 13,
	BurnNonFungible_GasPayer = 14,
	Phantasma = 15,
	Phantasma_Raw = 16,
}

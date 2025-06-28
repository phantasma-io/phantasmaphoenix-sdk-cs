namespace PhantasmaPhoenix.Protocol;

public enum EventKind
{
	Unknown = 0,
	ChainCreate = 1,
	TokenCreate = 2,
	TokenSend = 3,
	TokenReceive = 4,
	TokenMint = 5,
	TokenBurn = 6,
	TokenStake = 7,
	TokenClaim = 8,
	AddressRegister = 9,
	AddressLink = 10,
	AddressUnlink = 11,
	OrganizationCreate = 12,
	OrganizationAdd = 13,
	OrganizationRemove = 14,
	GasEscrow = 15,
	GasPayment = 16,
	AddressUnregister = 17,
	OrderCreated = 18,
	OrderCancelled = 19,
	OrderFilled = 20,
	OrderClosed = 21,
	FeedCreate = 22,
	FeedUpdate = 23,
	FileCreate = 24,
	FileDelete = 25,
	ValidatorPropose = 26,
	ValidatorElect = 27,
	ValidatorRemove = 28,
	ValidatorSwitch = 29,
	PackedNFT = 30,
	ValueCreate = 31,
	ValueUpdate = 32,
	PollCreated = 33,
	PollClosed = 34,
	PollVote = 35,
	ChannelCreate = 36,
	ChannelRefill = 37,
	ChannelSettle = 38,
	LeaderboardCreate = 39,
	LeaderboardInsert = 40,
	LeaderboardReset = 41,
	PlatformCreate = 42,
	ChainSwap = 43,
	ContractRegister = 44,
	ContractDeploy = 45,
	AddressMigration = 46,
	ContractUpgrade = 47,
	Log = 48,
	Inflation = 49,
	OwnerAdded = 50,
	OwnerRemoved = 51,
	DomainCreate = 52,
	DomainDelete = 53,
	TaskStart = 54,
	TaskStop = 55,
	CrownRewards = 56,
	Infusion = 57,
	Crowdsale = 58,
	OrderBid = 59,
	ContractKill = 60,
	OrganizationKill = 61,
	MasterClaim = 62,
	ExecutionFailure = 63,
	Custom = 64,
	Custom_V2 = 65,
	GovernanceSetGasEvent = 66
}

public enum ExecutionState
{
	Running,
	Break,
	Fault,
	Halt
}

public enum ProofOfWork
{
	None = 0,
	Minimal = 5,
	Moderate = 15,
	Hard = 19,
	Heavy = 24,
	Extreme = 30
}

public enum NativeContractKind
{
	Gas,
	Block,
	Stake,
	Swap,
	Account,
	Consensus,
	Governance,
	Storage,
	Validator,
	Interop,
	Exchange,
	Privacy,
	Relay,
	Ranking,
	Market,
	Friends,
	Mail,
	Sale,
	Unknown,
}

public enum Nexus
{
	Mainnet,
	Testnet,
	Simnet
}

[Flags]
public enum TokenFlags
{
	None = 0,
	Transferable = 1 << 0,
	Fungible = 1 << 1,
	Finite = 1 << 2,
	Divisible = 1 << 3,
	Fuel = 1 << 4,
	Stakable = 1 << 5,
	Fiat = 1 << 6,
	Swappable = 1 << 7,
	Burnable = 1 << 8,
}

public enum TypeAuction
{
    Fixed = 0,
    Classic = 1,
    Reserve = 2,
    Dutch = 3,
}

public enum SaleEventKind
{
    Creation,
    SoftCap,
    HardCap,
    AddedToWhitelist,
    RemovedFromWhitelist,
    Distribution,
    Refund,
    PriceChange,
    Participation,
}

using System.Globalization;
using System.Text;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.RPC.Types;

namespace PhantasmaPhoenix.RPC;

public sealed class SendTransactionOptions : PlanRequestOptions
{
	/// <summary>
	/// For a token creation, ask the chain whether the symbol is already taken. The send goes ahead only
	/// when the chain answered that the symbol is free, see
	/// <see cref="TransactionPreflight.PreflightAsync"/>. Every other message is unaffected. Default
	/// true. False skips the lookup.
	/// <para>
	/// The send is also refused when the lookup answered nothing at all. The policy fee is spent before
	/// the contract looks at the symbol, so sending on an unestablished state is the outcome worth
	/// paying a round trip to avoid.
	/// </para>
	/// </summary>
	public bool Preflight { get; set; } = true;
}

/// <summary>
/// Plain .NET RPC wrapper for Phantasma API using RpcClient with async and await
/// </summary>
public class PhantasmaAPI : IDisposable, IGasConfigSource, IPreflightSource, IInfusionSource
{
	/// <summary>RPC endpoint URL like https://testnet.phantasma.info/rpc</summary>
	public string Host { get; }

	private readonly RpcClient _rpc;
	private readonly bool _ownsRpcClient;

	/// <summary>
	/// Creates a new Phantasma API wrapper using the given RpcClient or a new internal instance
	/// </summary>
	/// <param name="host">RPC endpoint URL</param>
	/// <param name="rpcClient">Optional RpcClient to use - if null a new instance will be created and disposed by this object</param>
	/// <param name="apiKey">Optional X-Api-Key sent on every request, used only when this wrapper creates its own RpcClient</param>
	public PhantasmaAPI(string host, RpcClient? rpcClient = null, string? apiKey = null)
	{
		Host = host ?? throw new ArgumentNullException(nameof(host));

		if (rpcClient == null)
		{
			_rpc = new RpcClient(apiKey: apiKey);
			_ownsRpcClient = true;
		}
		else
		{
			_rpc = rpcClient;
			_ownsRpcClient = false;
		}
	}

	/// <summary>
	/// Disposes this API instance and the internal RpcClient if owned
	/// </summary>
	public void Dispose()
	{
		if (_ownsRpcClient)
		{
			_rpc?.Dispose();
		}
	}

	#region Account

	/// <summary>
	/// Gets a lightweight account overview (name and staking) for the specified address
	/// </summary>
	/// <remarks>
	/// Cost is independent of how much the address holds, which makes this the safe replacement for
	/// <see cref="GetAccountAsync(string)"/> in wallet refresh loops. Balances and NFTs are fetched
	/// separately through the cursor-paginated account endpoints.
	/// </remarks>
	/// <param name="account">Account address</param>
	/// <returns>Account overview or null if not found</returns>
	public Task<AccountInfoResult?> GetAccountInfoAsync(string account) =>
		_rpc.SendRpcAsync<AccountInfoResult>(Host, "getAccountInfo", account);

	/// <summary>
	/// Gets a lightweight account overview (name and staking) for the specified address and address type
	/// </summary>
	/// <param name="account">Account address text</param>
	/// <param name="checkAddressReservedByte">True to validate address reserved byte</param>
	/// <param name="addressType">Account address type</param>
	/// <returns>Account overview or null if not found</returns>
	public Task<AccountInfoResult?> GetAccountInfoAsync(
		string account,
		bool checkAddressReservedByte,
		RpcAddressType addressType) =>
		_rpc.SendRpcAsync<AccountInfoResult>(Host, "getAccountInfo", account, checkAddressReservedByte, addressType);

	/// <summary>
	/// Gets lightweight account overviews (name and staking) for a batch of addresses in one call
	/// </summary>
	/// <remarks>
	/// Batch counterpart of <see cref="GetAccountInfoAsync(string)"/> with the same per-account
	/// record: the node answers every address from a single state snapshot and returns results in
	/// request order. At most 100 addresses per request; the batch is rejected as a whole when any
	/// address is malformed. The addresses travel as a native JSON array parameter.
	/// </remarks>
	/// <param name="accounts">Account addresses, at most 100 per request</param>
	/// <returns>Account overviews in request order or null</returns>
	public Task<AccountInfoResult[]?> GetAccountInfosAsync(string[] accounts) =>
		_rpc.SendRpcAsync<AccountInfoResult[]>(Host, "getAccountInfos", new object[] { accounts ?? Array.Empty<string>() });

	/// <summary>
	/// Gets lightweight account overviews (name and staking) for a batch of addresses of the same address type
	/// </summary>
	/// <param name="accounts">Account addresses, at most 100 per request</param>
	/// <param name="checkAddressReservedByte">True to validate address reserved byte</param>
	/// <param name="addressType">Account address type</param>
	/// <returns>Account overviews in request order or null</returns>
	public Task<AccountInfoResult[]?> GetAccountInfosAsync(
		string[] accounts,
		bool checkAddressReservedByte,
		RpcAddressType addressType) =>
		_rpc.SendRpcAsync<AccountInfoResult[]>(Host, "getAccountInfos", accounts ?? Array.Empty<string>(), checkAddressReservedByte, addressType);

	/// <summary>
	/// Gets account information, including balances, for the specified address
	/// </summary>
	/// <remarks>
	/// Deprecated. The response embeds every NFT id the address owns, so it grows without bound with
	/// the size of the account; the node caps each <c>Balances[].Ids</c> list at 10000 entries while
	/// <c>Balances[].Amount</c> keeps the true count, meaning the list is silently partial for large
	/// holders. Use <see cref="GetAccountInfoAsync(string)"/> together with
	/// <see cref="GetAccountFungibleTokensAsync(string, string, ulong, uint, string, bool)"/> and
	/// <see cref="GetAccountNFTsAsync(string, string, ulong, uint, uint, string, bool, bool)"/>.
	/// </remarks>
	/// <param name="address">Account address</param>
	/// <returns>Account data or null if not found</returns>
	[Obsolete("Use GetAccountInfoAsync with GetAccountFungibleTokensAsync/GetAccountNFTsAsync; getAccount caps Balances[].Ids at 10000.")]
	public Task<AccountResult?> GetAccountAsync(string address) =>
		_rpc.SendRpcAsync<AccountResult>(Host, "getAccount", address);

	/// <summary>
	/// Gets account information, including balances, for the specified address and address type
	/// </summary>
	/// <remarks>Deprecated - see <see cref="GetAccountAsync(string)"/>.</remarks>
	/// <param name="address">Account address text</param>
	/// <param name="extended">Deprecated server flag; kept for parity</param>
	/// <param name="checkAddressReservedByte">True to validate address reserved byte</param>
	/// <param name="addressType">Account address type</param>
	/// <returns>Account data or null if not found</returns>
	[Obsolete("Use GetAccountInfoAsync with GetAccountFungibleTokensAsync/GetAccountNFTsAsync; getAccount caps Balances[].Ids at 10000.")]
	public Task<AccountResult?> GetAccountAsync(
		string address,
		bool extended,
		bool checkAddressReservedByte,
		RpcAddressType addressType) =>
		_rpc.SendRpcAsync<AccountResult>(Host, "getAccount", address, extended, checkAddressReservedByte, addressType);

	/// <summary>
	/// Gets account information for multiple addresses
	/// </summary>
	/// <remarks>
	/// Deprecated for the same reason as <see cref="GetAccountAsync(string)"/>, multiplied by the
	/// number of addresses in one call.
	/// </remarks>
	/// <param name="addresses">Array of account addresses</param>
	/// <returns>Array of account results or null</returns>
	[Obsolete("Use GetAccountInfoAsync with GetAccountFungibleTokensAsync/GetAccountNFTsAsync; getAccounts caps Balances[].Ids at 10000.")]
	public Task<AccountResult[]?> GetAccountsAsync(string[] addresses) =>
		_rpc.SendRpcAsync<AccountResult[]>(Host, "getAccounts", string.Join(",", addresses ?? Array.Empty<string>()));

	/// <summary>
	/// Gets account information for multiple addresses of the same address type
	/// </summary>
	/// <remarks>Deprecated - see <see cref="GetAccountsAsync(string[])"/>.</remarks>
	/// <param name="addresses">Array of account addresses</param>
	/// <param name="extended">Deprecated server flag; kept for parity</param>
	/// <param name="checkAddressReservedByte">True to validate address reserved byte</param>
	/// <param name="addressType">Account address type</param>
	/// <returns>Array of account results or null</returns>
	[Obsolete("Use GetAccountInfoAsync with GetAccountFungibleTokensAsync/GetAccountNFTsAsync; getAccounts caps Balances[].Ids at 10000.")]
	public Task<AccountResult[]?> GetAccountsAsync(
		string[] addresses,
		bool extended,
		bool checkAddressReservedByte,
		RpcAddressType addressType) =>
		_rpc.SendRpcAsync<AccountResult[]>(
			Host,
			"getAccounts",
			string.Join(",", addresses ?? Array.Empty<string>()),
			extended,
			checkAddressReservedByte,
			addressType);

	/// <summary>
	/// Looks up an address by name
	/// </summary>
	/// <param name="name">Registered name to resolve</param>
	/// <returns>Resolved address text or null</returns>
	public Task<string?> LookUpNameAsync(string name) =>
		_rpc.SendRpcAsync<string>(Host, "lookUpName", name);

	/// <summary>
	/// Gets fungible token balances owned by an address (cursor pagination)
	/// </summary>
	/// <param name="account">Account address</param>
	/// <param name="tokenSymbol">Token symbol filter (optional)</param>
	/// <param name="carbonTokenId">Carbon token id filter (optional)</param>
	/// <param name="pageSize">Items per page; the node accepts 1..100 and rejects anything outside that range</param>
	/// <param name="cursor">Cursor for the next page</param>
	/// <param name="checkAddressReservedByte">True to validate address reserved byte</param>
	/// <returns>Cursor paginated balances or null</returns>
	public Task<CursorPaginatedResult<BalanceResult[]>?> GetAccountFungibleTokensAsync(
		string account,
		string tokenSymbol = "",
		ulong carbonTokenId = 0,
		uint pageSize = 10,
		string cursor = "",
		bool checkAddressReservedByte = true) =>
		_rpc.SendRpcAsync<CursorPaginatedResult<BalanceResult[]>>(
			Host,
			"getAccountFungibleTokens",
			account,
			tokenSymbol,
			carbonTokenId,
			pageSize,
			cursor,
			checkAddressReservedByte);

	/// <summary>
	/// Gets fungible token balances owned by an address of a specific address type (cursor pagination)
	/// </summary>
	/// <param name="account">Account address</param>
	/// <param name="tokenSymbol">Token symbol filter (optional)</param>
	/// <param name="carbonTokenId">Carbon token id filter (optional)</param>
	/// <param name="pageSize">Items per page; the node accepts 1..100 and rejects anything outside that range</param>
	/// <param name="cursor">Cursor for the next page</param>
	/// <param name="checkAddressReservedByte">True to validate address reserved byte</param>
	/// <param name="addressType">Account address type</param>
	/// <returns>Cursor paginated balances or null</returns>
	public Task<CursorPaginatedResult<BalanceResult[]>?> GetAccountFungibleTokensAsync(
		string account,
		string tokenSymbol,
		ulong carbonTokenId,
		uint pageSize,
		string cursor,
		bool checkAddressReservedByte,
		RpcAddressType addressType) =>
		_rpc.SendRpcAsync<CursorPaginatedResult<BalanceResult[]>>(
			Host,
			"getAccountFungibleTokens",
			account,
			tokenSymbol,
			carbonTokenId,
			pageSize,
			cursor,
			checkAddressReservedByte,
			addressType);

	/// <summary>
	/// Gets NFTs owned by an address (cursor pagination)
	/// </summary>
	/// <param name="account">Account address</param>
	/// <param name="tokenSymbol">Token symbol filter (optional)</param>
	/// <param name="carbonTokenId">Carbon token id filter (optional)</param>
	/// <param name="carbonSeriesId">Carbon series id filter (optional)</param>
	/// <param name="pageSize">Items per page; the node accepts 1..100, or 1..50 when <paramref name="extended"/> is true</param>
	/// <param name="cursor">Cursor for the next page</param>
	/// <param name="extended">True to include properties</param>
	/// <param name="checkAddressReservedByte">True to validate address reserved byte</param>
	/// <returns>Cursor paginated NFT data or null</returns>
	public Task<CursorPaginatedResult<TokenDataResult[]>?> GetAccountNFTsAsync(
		string account,
		string tokenSymbol = "",
		ulong carbonTokenId = 0,
		uint carbonSeriesId = 0,
		uint pageSize = 10,
		string cursor = "",
		bool extended = false,
		bool checkAddressReservedByte = true) =>
		_rpc.SendRpcAsync<CursorPaginatedResult<TokenDataResult[]>>(
			Host,
			"getAccountNFTs",
			account,
			tokenSymbol,
			carbonTokenId,
			carbonSeriesId,
			pageSize,
			cursor,
			extended,
			checkAddressReservedByte);

	/// <summary>
	/// Gets NFTs owned by an address of a specific address type (cursor pagination)
	/// </summary>
	/// <param name="account">Account address</param>
	/// <param name="tokenSymbol">Token symbol filter (optional)</param>
	/// <param name="carbonTokenId">Carbon token id filter (optional)</param>
	/// <param name="carbonSeriesId">Carbon series id filter (optional)</param>
	/// <param name="pageSize">Items per page; the node accepts 1..100, or 1..50 when <paramref name="extended"/> is true</param>
	/// <param name="cursor">Cursor for the next page</param>
	/// <param name="extended">True to include properties</param>
	/// <param name="checkAddressReservedByte">True to validate address reserved byte</param>
	/// <param name="addressType">Account address type</param>
	/// <returns>Cursor paginated NFT data or null</returns>
	public Task<CursorPaginatedResult<TokenDataResult[]>?> GetAccountNFTsAsync(
		string account,
		string tokenSymbol,
		ulong carbonTokenId,
		uint carbonSeriesId,
		uint pageSize,
		string cursor,
		bool extended,
		bool checkAddressReservedByte,
		RpcAddressType addressType) =>
		_rpc.SendRpcAsync<CursorPaginatedResult<TokenDataResult[]>>(
			Host,
			"getAccountNFTs",
			account,
			tokenSymbol,
			carbonTokenId,
			carbonSeriesId,
			pageSize,
			cursor,
			extended,
			checkAddressReservedByte,
			addressType);

	/// <summary>
	/// Gets NFT tokens for which the account owns at least one NFT instance (cursor pagination)
	/// </summary>
	/// <param name="account">Account address</param>
	/// <param name="tokenSymbol">Token symbol filter (optional)</param>
	/// <param name="carbonTokenId">Carbon token id filter (optional)</param>
	/// <param name="pageSize">Items per page; the node accepts 1..100 and rejects anything outside that range</param>
	/// <param name="cursor">Cursor for the next page</param>
	/// <param name="checkAddressReservedByte">True to validate address reserved byte</param>
	/// <returns>Cursor paginated token data or null</returns>
	public Task<CursorPaginatedResult<TokenResult[]>?> GetAccountOwnedTokensAsync(
		string account,
		string tokenSymbol = "",
		ulong carbonTokenId = 0,
		uint pageSize = 10,
		string cursor = "",
		bool checkAddressReservedByte = true) =>
		_rpc.SendRpcAsync<CursorPaginatedResult<TokenResult[]>>(
			Host,
			"getAccountOwnedTokens",
			account,
			tokenSymbol,
			carbonTokenId,
			pageSize,
			cursor,
			checkAddressReservedByte);

	/// <summary>
	/// Gets NFT tokens for which the account owns at least one NFT instance for a specific address type (cursor pagination)
	/// </summary>
	/// <param name="account">Account address</param>
	/// <param name="tokenSymbol">Token symbol filter (optional)</param>
	/// <param name="carbonTokenId">Carbon token id filter (optional)</param>
	/// <param name="pageSize">Items per page; the node accepts 1..100 and rejects anything outside that range</param>
	/// <param name="cursor">Cursor for the next page</param>
	/// <param name="checkAddressReservedByte">True to validate address reserved byte</param>
	/// <param name="addressType">Account address type</param>
	/// <returns>Cursor paginated token data or null</returns>
	public Task<CursorPaginatedResult<TokenResult[]>?> GetAccountOwnedTokensAsync(
		string account,
		string tokenSymbol,
		ulong carbonTokenId,
		uint pageSize,
		string cursor,
		bool checkAddressReservedByte,
		RpcAddressType addressType) =>
		_rpc.SendRpcAsync<CursorPaginatedResult<TokenResult[]>>(
			Host,
			"getAccountOwnedTokens",
			account,
			tokenSymbol,
			carbonTokenId,
			pageSize,
			cursor,
			checkAddressReservedByte,
			addressType);

	/// <summary>
	/// Gets NFT series for which the account owns at least one NFT instance (cursor pagination)
	/// </summary>
	/// <param name="account">Account address</param>
	/// <param name="tokenSymbol">Token symbol filter (optional)</param>
	/// <param name="carbonTokenId">Carbon token id filter (optional)</param>
	/// <param name="pageSize">Items per page; the node accepts 1..100 and rejects anything outside that range</param>
	/// <param name="cursor">Cursor for the next page</param>
	/// <param name="checkAddressReservedByte">True to validate address reserved byte</param>
	/// <returns>Cursor paginated series data or null</returns>
	public Task<CursorPaginatedResult<TokenSeriesResult[]>?> GetAccountOwnedTokenSeriesAsync(
		string account,
		string tokenSymbol = "",
		ulong carbonTokenId = 0,
		uint pageSize = 10,
		string cursor = "",
		bool checkAddressReservedByte = true) =>
		_rpc.SendRpcAsync<CursorPaginatedResult<TokenSeriesResult[]>>(
			Host,
			"getAccountOwnedTokenSeries",
			account,
			tokenSymbol,
			carbonTokenId,
			pageSize,
			cursor,
			checkAddressReservedByte);

	/// <summary>
	/// Gets NFT series for which the account owns at least one NFT instance for a specific address type (cursor pagination)
	/// </summary>
	/// <param name="account">Account address</param>
	/// <param name="tokenSymbol">Token symbol filter (optional)</param>
	/// <param name="carbonTokenId">Carbon token id filter (optional)</param>
	/// <param name="pageSize">Items per page; the node accepts 1..100 and rejects anything outside that range</param>
	/// <param name="cursor">Cursor for the next page</param>
	/// <param name="checkAddressReservedByte">True to validate address reserved byte</param>
	/// <param name="addressType">Account address type</param>
	/// <returns>Cursor paginated series data or null</returns>
	public Task<CursorPaginatedResult<TokenSeriesResult[]>?> GetAccountOwnedTokenSeriesAsync(
		string account,
		string tokenSymbol,
		ulong carbonTokenId,
		uint pageSize,
		string cursor,
		bool checkAddressReservedByte,
		RpcAddressType addressType) =>
		_rpc.SendRpcAsync<CursorPaginatedResult<TokenSeriesResult[]>>(
			Host,
			"getAccountOwnedTokenSeries",
			account,
			tokenSymbol,
			carbonTokenId,
			pageSize,
			cursor,
			checkAddressReservedByte,
			addressType);

	#endregion

	#region Auction

	/// <summary>
	/// Gets the number of auctions currently available in the market contract for a given token
	/// </summary>
	/// <param name="chainAddressOrName">Chain address or name</param>
	/// <param name="symbol">Token symbol</param>
	/// <returns>Auctions count as integer</returns>
	public async Task<int> GetAuctionsCountAsync(string chainAddressOrName, string symbol)
	{
		var s = await _rpc.SendRpcAsync<string>(Host, "getAuctionsCount", chainAddressOrName, symbol);
		return int.Parse(s ?? "0", CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// Gets all auctions currently available in the market contract for a given token, with pagination
	/// </summary>
	/// <param name="chainAddressOrName">Chain address or name</param>
	/// <param name="symbol">Token symbol</param>
	/// <param name="page">Page number starting at 1</param>
	/// <param name="pageSize">Items per page</param>
	/// <returns>Tuple with results array, current page, total items and total pages</returns>
	public async Task<(AuctionResult[]? result, uint page, uint total, uint totalPages)>
		GetAuctionsAsync(string chainAddressOrName, string symbol, uint page, uint pageSize)
	{
		var res = await _rpc.SendRpcAsync<PaginatedResult<AuctionResult[]>>(
			Host, "getAuctions", chainAddressOrName, symbol, page, pageSize);
		return (res?.Result, res?.Page ?? 0, res?.Total ?? 0, res?.TotalPages ?? 0);
	}

	/// <summary>
	/// Gets a single auction by symbol and auction id
	/// </summary>
	/// <param name="chainAddressOrName">Chain address or name</param>
	/// <param name="symbol">Token symbol</param>
	/// <param name="id">Auction id</param>
	/// <returns>Auction data or null</returns>
	public Task<AuctionResult?> GetAuctionAsync(string chainAddressOrName, string symbol, string id) =>
		_rpc.SendRpcAsync<AuctionResult>(Host, "getAuction", chainAddressOrName, symbol, id);

	#endregion

	#region Block

	/// <summary>
	/// Gets the latest block height for a chain
	/// </summary>
	/// <param name="chain">Chain name</param>
	/// <returns>Block height as long</returns>
	public async Task<long> GetBlockHeightAsync(string chain)
	{
		var s = await _rpc.SendRpcAsync<string>(Host, "getBlockHeight", chain);
		return long.Parse(s ?? "0", CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// Gets the number of transactions in a block by block hash
	/// </summary>
	/// <param name="chainAddressOrName">Chain address or name</param>
	/// <param name="blockHash">Block hash</param>
	/// <returns>Transaction count</returns>
	public async Task<int> GetBlockTransactionCountByHashAsync(string chainAddressOrName, string blockHash)
	{
		var s = await _rpc.SendRpcAsync<string>(Host, "getBlockTransactionCountByHash", chainAddressOrName, blockHash);
		return int.Parse(s ?? "0", CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// Gets a block by its hash
	/// </summary>
	/// <param name="blockHash">Block hash</param>
	/// <returns>Block data or null</returns>
	public Task<BlockResult?> GetBlockByHashAsync(string blockHash) =>
		_rpc.SendRpcAsync<BlockResult>(Host, "getBlockByHash", blockHash);

	/// <summary>
	/// Gets a block by chain and height
	/// </summary>
	/// <param name="chain">Chain name</param>
	/// <param name="height">Block height</param>
	/// <returns>Block data or null</returns>
	public Task<BlockResult?> GetBlockByHeightAsync(string chain, long height) =>
		_rpc.SendRpcAsync<BlockResult>(Host, "getBlockByHeight", chain, height.ToString());

	/// <summary>
	/// Gets the latest block for a chain
	/// </summary>
	/// <param name="chain">Chain name</param>
	/// <returns>Latest block data or null</returns>
	public Task<BlockResult?> GetLatestBlockAsync(string chain) =>
		_rpc.SendRpcAsync<BlockResult>(Host, "getLatestBlock", chain);

	/// <summary>
	/// Gets a transaction by chain, block hash, and transaction index
	/// </summary>
	/// <param name="chainAddressOrName">Chain address or name</param>
	/// <param name="blockHash">Block hash</param>
	/// <param name="index">Transaction index within the block</param>
	/// <returns>Transaction data or null</returns>
	public Task<TransactionResult?> GetTransactionByBlockHashAndIndexAsync(
		string chainAddressOrName,
		string blockHash,
		int index) =>
		_rpc.SendRpcAsync<TransactionResult>(
			Host,
			"getTransactionByBlockHashAndIndex",
			chainAddressOrName,
			blockHash,
			index);

	#endregion

	#region Chain

	/// <summary>
	/// Gets an array of all chains deployed on Phantasma
	/// </summary>
	/// <remarks>Warning: this Phantasma RPC method is currently stubbed and returns an empty array until backend support is completed.</remarks>
	/// <returns>Array of chains or null</returns>
	public Task<ChainResult[]?> GetChainsAsync() =>
		_rpc.SendRpcAsync<ChainResult[]>(Host, "getChains");

	/// <summary>
	/// Gets chain information by name
	/// </summary>
	/// <remarks>Warning: this Phantasma RPC method is currently stubbed and returns a default chain result until backend support is completed.</remarks>
	/// <param name="name">Chain name</param>
	/// <param name="extended">True to include extended data</param>
	/// <returns>Chain data or null</returns>
	public Task<ChainResult?> GetChainAsync(string name = "main", bool extended = true) =>
		_rpc.SendRpcAsync<ChainResult>(Host, "getChain", name, extended);

	/// <summary>
	/// Gets the current on-chain gas configuration plus the chain parameters fee estimation
	/// needs (block rate target, expiry window). Changes only via governance resolutions, so the
	/// result is safe to cache. Feed <see cref="GasConfigResult.ToGasConfig"/> into
	/// NativeFeeEstimator for Tier-1 fee estimates.
	/// </summary>
	/// <returns>Gas configuration or null</returns>
	public Task<GasConfigResult?> GetGasConfigAsync() =>
		_rpc.SendRpcAsync<GasConfigResult>(Host, "getGasConfig");

	#endregion

	#region Contract

	/// <summary>
	/// Gets contract metadata by name from the main chain
	/// </summary>
	/// <param name="contractName">Contract name</param>
	/// <returns>Contract data or null</returns>
	public Task<ContractResult?> GetContractAsync(string contractName) =>
		_rpc.SendRpcAsync<ContractResult>(Host, "getContract", PhantasmaPhoenix.Protocol.DomainSettings.RootChainName, contractName);

	/// <summary>
	/// Gets contract metadata by address from a specific chain
	/// </summary>
	/// <param name="chainAddressOrName">Chain address or name</param>
	/// <param name="contractAddress">Contract address</param>
	/// <returns>Contract data or null</returns>
	public Task<ContractResult?> GetContractByAddressAsync(string chainAddressOrName, string contractAddress) =>
		_rpc.SendRpcAsync<ContractResult>(Host, "getContractByAddress", chainAddressOrName, contractAddress);

	/// <summary>
	/// Gets all contracts deployed on the main chain
	/// </summary>
	/// <returns>Array of contracts or null</returns>
	public Task<ContractResult[]?> GetContractsAsync() =>
		_rpc.SendRpcAsync<ContractResult[]>(Host, "getContracts", PhantasmaPhoenix.Protocol.DomainSettings.RootChainName);

	#endregion

	#region Leaderboard

	/// <summary>
	/// Gets a leaderboard by name
	/// </summary>
	/// <remarks>Warning: this Phantasma RPC method is currently stubbed and returns a default leaderboard result until backend support is completed.</remarks>
	/// <param name="name">Leaderboard name</param>
	/// <returns>Leaderboard data or null</returns>
	public Task<LeaderboardResult?> GetLeaderboardAsync(string name) =>
		_rpc.SendRpcAsync<LeaderboardResult>(Host, "getLeaderboard", name);

	#endregion

	#region Nexus

	/// <summary>
	/// Gets nexus metadata including an array of all chains deployed on Phantasma
	/// </summary>
	/// <remarks>Warning: this Phantasma RPC method is currently stubbed and returns a default nexus result until backend support is completed.</remarks>
	/// <returns>Nexus data or null</returns>
	public Task<NexusResult?> GetNexusAsync() =>
		_rpc.SendRpcAsync<NexusResult>(Host, "getNexus");

	#endregion

	#region Organization

	/// <summary>
	/// Gets organization data by name
	/// </summary>
	/// <param name="name">Organization name, for example "masters"</param>
	/// <param name="includeMemberCount">True to include the member count; this requires a server-side member range scan</param>
	/// <returns>Organization data or null</returns>
	public Task<OrganizationResult?> GetOrganizationAsync(string name, bool includeMemberCount = false) =>
		_rpc.SendRpcAsync<OrganizationResult>(Host, "getOrganization", name, includeMemberCount);

	/// <summary>
	/// Gets organizations deployed on Phantasma using cursor pagination
	/// </summary>
	/// <param name="pageSize">Items per page; the node accepts 1..100 and rejects anything outside that range</param>
	/// <param name="cursor">Cursor for the next page</param>
	/// <param name="includeMemberCount">True to include the member count; this requires a server-side member range scan per organization</param>
	/// <returns>Cursor paginated organizations or null</returns>
	public Task<CursorPaginatedResult<OrganizationResult[]>?> GetOrganizationsAsync(
		uint pageSize = 10,
		string cursor = "",
		bool includeMemberCount = false) =>
		_rpc.SendRpcAsync<CursorPaginatedResult<OrganizationResult[]>>(
			Host,
			"getOrganizations",
			pageSize,
			cursor,
			includeMemberCount);

	/// <summary>
	/// Gets members of an organization by name using cursor pagination
	/// </summary>
	/// <param name="name">Organization name, for example "masters"</param>
	/// <param name="pageSize">Items per page; the node accepts 1..100 and rejects anything outside that range</param>
	/// <param name="cursor">Cursor for the next page</param>
	/// <param name="includeMemberTime">True to include each member join timestamp</param>
	/// <returns>Cursor paginated organization members or null</returns>
	public Task<CursorPaginatedResult<OrganizationMemberResult[]>?> GetOrganizationMembersAsync(
		string name,
		uint pageSize = 10,
		string cursor = "",
		bool includeMemberTime = true) =>
		_rpc.SendRpcAsync<CursorPaginatedResult<OrganizationMemberResult[]>>(
			Host,
			"getOrganizationMembers",
			name,
			pageSize,
			cursor,
			includeMemberTime);

	/// <summary>
	/// Gets membership status for an address in an organization by name
	/// </summary>
	/// <param name="name">Organization name, for example "masters"</param>
	/// <param name="address">Address to check</param>
	/// <param name="checkAddressReservedByte">True to validate the address reserved byte</param>
	/// <param name="addressType">Address text format</param>
	/// <returns>Organization member status or null</returns>
	public Task<OrganizationMemberResult?> GetOrganizationMemberAsync(
		string name,
		string address,
		bool checkAddressReservedByte = true,
		RpcAddressType addressType = RpcAddressType.Phantasma) =>
		_rpc.SendRpcAsync<OrganizationMemberResult>(
			Host,
			"getOrganizationMember",
			name,
			address,
			checkAddressReservedByte,
			addressType);

	#endregion

	#region Token

	/// <summary>
	/// Gets token metadata by symbol
	/// </summary>
	/// <param name="symbol">Token symbol</param>
	/// <returns>Token data or null</returns>
	public Task<TokenResult?> GetTokenAsync(string symbol) =>
		_rpc.SendRpcAsync<TokenResult>(Host, "getToken", symbol);

	/// <summary>
	/// Gets token metadata by symbol or carbon token id
	/// </summary>
	/// <param name="symbol">Token symbol (optional when using carbonTokenId)</param>
	/// <param name="extended">True to include extended data</param>
	/// <param name="carbonTokenId">Carbon token id (optional when using symbol)</param>
	/// <returns>Token data or null</returns>
	public Task<TokenResult?> GetTokenAsync(string symbol, bool extended, ulong carbonTokenId) =>
		_rpc.SendRpcAsync<TokenResult>(Host, "getToken", symbol, extended, carbonTokenId);

	/// <summary>
	/// Gets an array of all tokens deployed on Phantasma
	/// </summary>
	/// <returns>Array of token metadata or null</returns>
	public Task<TokenResult[]?> GetTokensAsync() =>
		_rpc.SendRpcAsync<TokenResult[]>(Host, "getTokens");

	/// <summary>
	/// Gets an array of all tokens deployed on Phantasma with optional owner filtering
	/// </summary>
	/// <param name="extended">True to include extended data. Deprecated server-side and slated for removal.</param>
	/// <param name="ownerAddress">Optional owner address filter</param>
	/// <returns>Array of token metadata or null</returns>
	public Task<TokenResult[]?> GetTokensAsync(bool extended, string? ownerAddress = null) =>
		_rpc.SendRpcAsync<TokenResult[]>(Host, "getTokens", extended, ownerAddress);

	/// <summary>
	/// Gets an array of all tokens deployed on Phantasma with optional owner filtering and explicit owner address type
	/// </summary>
	/// <param name="extended">True to include extended data. Deprecated server-side and slated for removal.</param>
	/// <param name="ownerAddress">Optional owner address filter</param>
	/// <param name="addressType">Owner address type</param>
	/// <returns>Array of token metadata or null</returns>
	public Task<TokenResult[]?> GetTokensAsync(bool extended, string? ownerAddress, RpcAddressType addressType) =>
		_rpc.SendRpcAsync<TokenResult[]>(Host, "getTokens", extended, ownerAddress, addressType);

	/// <summary>
	/// Gets token series for a token (cursor pagination)
	/// </summary>
	/// <param name="symbol">Token symbol (optional when using carbonTokenId)</param>
	/// <param name="carbonTokenId">Carbon token id (optional when using symbol)</param>
	/// <param name="pageSize">Items per page; the node accepts 1..100 and rejects anything outside that range</param>
	/// <param name="cursor">Cursor for the next page</param>
	/// <returns>Cursor paginated series data or null</returns>
	public Task<CursorPaginatedResult<TokenSeriesResult[]>?> GetTokenSeriesAsync(
		string symbol = "",
		ulong carbonTokenId = 0,
		uint pageSize = 10,
		string cursor = "") =>
		_rpc.SendRpcAsync<CursorPaginatedResult<TokenSeriesResult[]>>(
			Host,
			"getTokenSeries",
			symbol,
			carbonTokenId,
			pageSize,
			cursor);

	/// <summary>
	/// Gets a single token series by either Phantasma series id or Carbon series id.
	/// Provide one token identity: <paramref name="symbol"/> or <paramref name="carbonTokenId"/>.
	/// Provide one series identity: <paramref name="seriesId"/> or <paramref name="carbonSeriesId"/>.
	/// </summary>
	/// <param name="symbol">Token symbol (optional when using carbonTokenId)</param>
	/// <param name="carbonTokenId">Carbon token id (optional when using symbol)</param>
	/// <param name="seriesId">Phantasma series id (optional when using carbonSeriesId)</param>
	/// <param name="carbonSeriesId">Carbon series id (optional when using seriesId)</param>
	/// <returns>Single series entry or null</returns>
	public Task<TokenSeriesResult?> GetTokenSeriesByIdAsync(
		string symbol = "",
		ulong carbonTokenId = 0,
		string seriesId = "",
		uint carbonSeriesId = 0) =>
		_rpc.SendRpcAsync<TokenSeriesResult>(
			Host,
			"getTokenSeriesById",
			symbol,
			carbonTokenId,
			seriesId,
			carbonSeriesId);

	/// <summary>
	/// Gets NFTs for a token (cursor pagination)
	/// </summary>
	/// <param name="carbonTokenId">Carbon token id</param>
	/// <param name="carbonSeriesId">Carbon series id filter (optional)</param>
	/// <param name="pageSize">Items per page; the node accepts 1..100, or 1..50 when <paramref name="extended"/> is true</param>
	/// <param name="cursor">Cursor for the next page</param>
	/// <param name="extended">True to include properties</param>
	/// <param name="seriesId">Phantasma series id filter (optional). Appended at the end to preserve positional compatibility with older RPC servers.</param>
	/// <returns>Cursor paginated NFT data or null</returns>
	public Task<CursorPaginatedResult<TokenDataResult[]>?> GetTokenNFTsAsync(
		ulong carbonTokenId,
		uint carbonSeriesId = 0,
		uint pageSize = 10,
		string cursor = "",
		bool extended = false,
		string seriesId = "") =>
		_rpc.SendRpcAsync<CursorPaginatedResult<TokenDataResult[]>>(
			Host,
			"getTokenNFTs",
			carbonTokenId,
			carbonSeriesId,
			pageSize,
			cursor,
			extended,
			seriesId);

	/// <summary>
	/// Gets the token balance for a given address and token symbol
	/// </summary>
	/// <param name="address">Account address</param>
	/// <param name="symbol">Token symbol</param>
	/// <param name="chain">Chain name, default main</param>
	/// <returns>Balance data or null</returns>
	public Task<BalanceResult?> GetTokenBalanceAsync(string address, string symbol, string chain = "main") =>
		_rpc.SendRpcAsync<BalanceResult>(Host, "getTokenBalance", address, symbol, chain);

	/// <summary>
	/// Gets the token balance for a given address, token symbol and address type
	/// </summary>
	/// <param name="address">Account address</param>
	/// <param name="symbol">Token symbol</param>
	/// <param name="chain">Chain name, default main</param>
	/// <param name="checkAddressReservedByte">True to validate address reserved byte</param>
	/// <param name="addressType">Account address type</param>
	/// <returns>Balance data or null</returns>
	public Task<BalanceResult?> GetTokenBalanceAsync(
		string address,
		string symbol,
		string chain,
		bool checkAddressReservedByte,
		RpcAddressType addressType) =>
		_rpc.SendRpcAsync<BalanceResult>(Host, "getTokenBalance", address, symbol, chain, checkAddressReservedByte, addressType);

	/// <summary>
	/// Gets token data for a specific token id
	/// </summary>
	/// <remarks>
	/// Deprecated. The node serves this as a strict subset of getNFT - same response, with property
	/// loading forced off - so <see cref="GetNFTAsync(string, string, bool)"/> covers it entirely.
	/// </remarks>
	/// <param name="symbol">Token symbol</param>
	/// <param name="tokenId">Token id</param>
	/// <returns>Token data or null</returns>
	[Obsolete("Use GetNFTAsync; getTokenData is a strict subset of getNFT.")]
	public Task<TokenDataResult?> GetTokenDataAsync(string symbol, string tokenId) =>
		_rpc.SendRpcAsync<TokenDataResult>(Host, "getTokenData", symbol, tokenId);

	/// <summary>
	/// Gets NFT data and optionally loads properties
	/// </summary>
	/// <param name="symbol">NFT symbol</param>
	/// <param name="tokenId">Token id</param>
	/// <param name="loadProperties">True to load properties</param>
	/// <returns>NFT data or null</returns>
	public Task<TokenDataResult?> GetNFTAsync(string symbol, string tokenId, bool loadProperties) =>
		_rpc.SendRpcAsync<TokenDataResult>(Host, "getNFT", symbol, tokenId, loadProperties);

	/// <summary>
	/// Gets NFT data for multiple token ids
	/// </summary>
	/// <param name="symbol">NFT symbol</param>
	/// <param name="tokenIds">Array of token ids</param>
	/// <param name="extended">True to load properties</param>
	/// <returns>Array of NFT data or null</returns>
	public Task<TokenDataResult[]?> GetNFTsAsync(string symbol, IEnumerable<string> tokenIds, bool extended = false) =>
		_rpc.SendRpcAsync<TokenDataResult[]>(Host, "getNFTs", symbol, string.Join(",", tokenIds), extended);

	#endregion

	#region Transactions

	/// <summary>
	/// Gets address transactions with pagination
	/// </summary>
	/// <param name="address">Account address</param>
	/// <param name="page">Page number starting at 1</param>
	/// <param name="pageSize">Items per page</param>
	/// <returns>Tuple with result object, current page, total pages</returns>
	public async Task<(AccountTransactionsResult? result, uint page, uint totalPages)>
		GetAddressTransactionsAsync(string address, uint page, uint pageSize)
	{
		var res = await _rpc.SendRpcAsync<PaginatedResult<AccountTransactionsResult>>(
			Host, "getAddressTransactions", address, page, pageSize);
		return (res?.Result, res?.Page ?? 0, res?.TotalPages ?? 0);
	}

	/// <summary>
	/// Gets the number of transactions for an address on a chain
	/// </summary>
	/// <param name="address">Account address</param>
	/// <param name="chain">Chain name</param>
	/// <returns>Total number of transactions</returns>
	public async Task<int> GetAddressTransactionCountAsync(string address, string chain)
	{
		var s = await _rpc.SendRpcAsync<string>(Host, "getAddressTransactionCount", address, chain);
		return int.Parse(s ?? "0", CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// Gets a transaction by its hash if available
	/// </summary>
	/// <param name="txHash">Transaction hash</param>
	/// <returns>Transaction data or null</returns>
	public Task<TransactionResult?> GetTransactionAsync(string txHash) =>
		_rpc.SendRpcAsync<TransactionResult>(Host, "getTransaction", txHash);

	/// <summary>
	/// Broadcasts a transaction in hexadecimal encoding
	/// </summary>
	/// <param name="txData">Hex encoded transaction bytes</param>
	/// <returns>Transaction hash text or null</returns>
	public Task<string?> SendRawTransactionAsync(string txData) =>
		_rpc.SendRpcAsync<string>(Host, "sendRawTransaction", txData);

	/// <summary>
	/// Broadcasts a carbon transaction in hexadecimal encoding
	/// </summary>
	/// <param name="txData">Hex encoded carbon transaction bytes</param>
	/// <returns>Transaction hash text or null</returns>
	public Task<string?> SendCarbonTransactionAsync(string txData) =>
		_rpc.SendRpcAsync<string>(Host, "sendCarbonTransaction", txData);

	/// <summary>
	/// Invokes a VM script without state changes and returns its result
	/// </summary>
	/// <param name="chain">Chain name</param>
	/// <param name="scriptData">Hex encoded script bytes</param>
	/// <returns>Script invocation result or null</returns>
	public Task<ScriptResult?> InvokeRawScriptAsync(string chain, string scriptData) =>
		_rpc.SendRpcAsync<ScriptResult>(Host, "invokeRawScript", chain, scriptData);

	/// <summary>
	/// Dry-runs a serialized transaction envelope against current chain state and returns its
	/// exact fee bill with recommended maxGas/maxData ceilings (gas-model-v2 Tier-2 estimate).
	/// Signatures inside the envelope may be zero-filled dummies of the correct length - the
	/// simulation skips signature checks, and dummies preserve the exact envelope byte length the
	/// bill depends on. Until the estimate service is launched this method returns a standard RPC
	/// error; use the Tier-1 NativeFeeEstimator with getGasConfig as the fallback.
	/// </summary>
	/// <param name="txData">Hex encoded serialized transaction envelope (signed or dummy-signed)</param>
	/// <returns>Exact fee estimate or null</returns>
	public Task<EstimateTransactionResult?> EstimateTransactionAsync(string txData) =>
		_rpc.SendRpcAsync<EstimateTransactionResult>(Host, "estimateTransaction", txData);

	/// <summary>
	/// Builds, signs and broadcasts a transaction with a UTF8 payload
	/// </summary>
	/// <param name="keys">Key pair used to sign a transaction</param>
	/// <param name="nexus">Nexus name</param>
	/// <param name="script">Transaction script bytes</param>
	/// <param name="chain">Target chain name</param>
	/// <param name="payload">UTF8 payload string</param>
	/// <param name="customSignFunction">Optional custom signer that receives data, script and payload bytes</param>
	/// <param name="expiration">Moment the transaction stops being admissible. Default
	/// <see cref="TxLimits.DefaultExpiryMs"/> from now, the same lifetime a Carbon transaction gets. A flow
	/// with a person in it should take the chain's own window instead, see
	/// <see cref="TxLimits.ExpiryWithin"/></param>
	/// <returns>Transaction hash text or null</returns>
	/// <exception cref="Exception">Thrown when the hash returned by RPC does not match the locally computed hash</exception>
	public async Task<string?> SignAndSendTransactionAsync(
		IKeyPair keys, string nexus, byte[] script, string chain, string payload,
		Func<byte[], byte[], byte[], byte[]>? customSignFunction = null, Timestamp? expiration = null)
	{
		var bytes = string.IsNullOrEmpty(payload) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(payload);
		return await SignAndSendTransactionAsync(keys, nexus, script, chain, bytes, customSignFunction, expiration);
	}

	/// <summary>
	/// Builds, signs and broadcasts a transaction with a binary payload
	/// </summary>
	/// <param name="keys">Key pair used to sign a transaction</param>
	/// <param name="nexus">Nexus name</param>
	/// <param name="script">Transaction script bytes</param>
	/// <param name="chain">Target chain name</param>
	/// <param name="payload">Binary payload bytes</param>
	/// <param name="customSignFunction">Optional custom signer that receives data, script and payload bytes</param>
	/// <param name="expiration">Moment the transaction stops being admissible. Default
	/// <see cref="TxLimits.DefaultExpiryMs"/> from now, the same lifetime a Carbon transaction gets. A flow
	/// with a person in it should take the chain's own window instead, see
	/// <see cref="TxLimits.ExpiryWithin"/></param>
	/// <returns>Transaction hash text or null</returns>
	/// <exception cref="Exception">Thrown when the hash returned by RPC does not match the locally computed hash</exception>
	public async Task<string?> SignAndSendTransactionAsync(
		IKeyPair keys, string nexus, byte[] script, string chain, byte[] payload,
		Func<byte[], byte[], byte[], byte[]>? customSignFunction = null, Timestamp? expiration = null)
	{
		// This transaction carries its expiration in seconds. TxLimits counts in the milliseconds the
		// chain reads on the Carbon path, and both paths are admitted by the same check.
		var tx = new Protocol.Transaction(
			nexus, chain, script, expiration ?? new Timestamp((uint)(TxLimits.DefaultExpiry() / 1000)),
			payload ?? Array.Empty<byte>());

		var txHash = tx.Sign(keys, customSignFunction);

		var encoded = Base16.Encode(tx.ToByteArray(true));
		var txHashFromRpc = await SendRawTransactionAsync(encoded);

		if (txHash.ToString() != txHashFromRpc)
		{
			// TODO improve API calls to make this situation impossible
			throw new Exception($"Error: RPC returned different hash {txHashFromRpc}, expected {txHash}");
		}

		return txHashFromRpc;
	}

	private ChainFeePlanner? _fees;

	/// <summary>
	/// The fee planner of the chain this client talks to. It reads the chain's gas config through this
	/// client, caches it briefly, and prices messages with it. Call it as
	/// <c>api.Fees.PlanAsync(msg)</c>.
	/// </summary>
	public ChainFeePlanner Fees => _fees ??= new ChainFeePlanner(this);

	/// <summary>
	/// The gas token's id. The pre-flight uses it as its control lookup, because that token certainly
	/// exists on any live chain. The id comes from the same cached gas config the planner reads, so
	/// asking for it costs a round trip only once a minute. It is null when this client cannot read
	/// that config, and the pre-flight then reports <see cref="PreflightVerdict.Unknown"/>.
	/// </summary>
	public async Task<ulong?> ControlTokenIdAsync()
	{
		try
		{
			return (await Fees.ConfigAsync().ConfigureAwait(false)).gasTokenId;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Returns what NFT <paramref name="instanceId"/> of token <paramref name="tokenId"/> holds at its
	/// own address, in the form the fee planner prices. A burn of that NFT returns every one of these
	/// to the burner and pays for each.
	/// <para>
	/// The assets are read through the account queries, with the address in its Carbon form. Fungible
	/// balances are resolved to token ids, so the free rows of the gas and data tokens are recognised.
	/// Whether the burner already holds a returned token is left at the costlier reading, and that
	/// moves the escrow ceiling alone.
	/// </para>
	/// </summary>
	public async Task<IReadOnlyList<InfusedAsset>> InfusedAssetsAsync(ulong tokenId, ulong instanceId)
	{
		var address = TokenHelper.GetNftAddress(tokenId, instanceId).ToHex();
		var assets = new List<InfusedAsset>();
		var balances = await ReadAllPagesAsync(cursor => GetAccountFungibleTokensAsync(address, string.Empty, 0, 100, cursor, false, RpcAddressType.Carbon)).ConfigureAwait(false);
		foreach (var balance in balances)
		{
			var token = await GetTokenAsync(balance.Symbol).ConfigureAwait(false)
				?? throw new InvalidOperationException($"getToken returned nothing for {balance.Symbol}");
			assets.Add(new InfusedAsset { TokenId = ulong.Parse(token.CarbonId, CultureInfo.InvariantCulture), NonFungible = false });
		}
		var owned = await ReadAllPagesAsync(cursor => GetAccountOwnedTokensAsync(address, string.Empty, 0, 100, cursor, false, RpcAddressType.Carbon)).ConfigureAwait(false);
		foreach (var token in owned)
		{
			var balance = await GetTokenBalanceAsync(address, token.Symbol, "main", false, RpcAddressType.Carbon).ConfigureAwait(false)
				?? throw new InvalidOperationException($"getTokenBalance returned nothing for {token.Symbol}");
			assets.Add(new InfusedAsset
			{
				TokenId = ulong.Parse(token.CarbonId, CultureInfo.InvariantCulture),
				NonFungible = true,
				InstanceCount = (uint)ulong.Parse(balance.Amount, CultureInfo.InvariantCulture)
			});
		}
		return assets;
	}

	// Walks a cursor-paginated query to the end. The cursor the node returns drives the loop, and an
	// item count never does. The loop stops on a cursor it has already seen, and it stops past a page
	// cap. A misbehaving node therefore cannot keep it going forever.
	private static async Task<List<T>> ReadAllPagesAsync<T>(Func<string, Task<CursorPaginatedResult<T[]>?>> page)
	{
		const int maxPages = 1000;
		var items = new List<T>();
		var seen = new HashSet<string>();
		var cursor = string.Empty;
		for (var i = 0; i < maxPages; i++)
		{
			var result = await page(cursor).ConfigureAwait(false);
			if (result?.Result != null)
				items.AddRange(result.Result);
			if (string.IsNullOrEmpty(result?.Cursor) || !seen.Add(result!.Cursor!))
				return items;
			cursor = result.Cursor!;
		}
		throw new InvalidOperationException($"the node kept returning pages past {maxPages}");
	}

	/// <summary>
	/// Sends a message in one step. The steps are pre-flight, fee plan, signatures and broadcast.
	/// Returns the transaction hash.
	/// <para>
	/// A message whose <c>maxGas</c> is still zero is planned against this chain's prices, see
	/// <see cref="Fees"/>. A message the caller already planned is sent as it is. Every witness signs
	/// through its <see cref="ITxSigner"/>, which can hold keys, drive hardware or call a remote
	/// service.
	/// </para>
	/// <para>
	/// The pre-flight refuses a token creation whose symbol the chain says is taken. It also refuses
	/// one it could establish nothing about. See <see cref="SendTransactionOptions.Preflight"/>.
	/// </para>
	/// </summary>
	public async Task<string> SendTransactionAsync(TxMsg msg, IReadOnlyList<ITxSigner> signers, SendTransactionOptions? options = null, CancellationToken cancellationToken = default)
	{
		if (signers == null) throw new ArgumentNullException(nameof(signers));
		options ??= new SendTransactionOptions();
		if (options.Preflight)
		{
			var check = await TransactionPreflight.PreflightAsync(this, msg).ConfigureAwait(false);
			if (check.Verdict == PreflightVerdict.Taken)
				throw new TransactionPreflightException($"{check.Subject} is already taken");
			if (check.Verdict == PreflightVerdict.Unknown)
				throw new TransactionPreflightException($"Could not establish whether {check.Subject} is taken: {check.Reason}");
		}
		var planned = msg;
		if (msg.maxGas == 0)
		{
			// Only the witness-array types take their witness count from the caller. Every other type
			// fixes its own slots in the message, and one signer may fill two of them.
			var planOptions = (PlanRequestOptions)options.Clone();
			if (!planOptions.WitnessCount.HasValue && SignedTxMsg.RequiredWitnesses(msg) == null)
				planOptions.WitnessCount = signers.Count;
			planned = (await Fees.PlanAsync(msg, planOptions).ConfigureAwait(false)).Apply(msg);
		}
		var bytes = await TxMsgSigner.SignAndSerializeWithAsync(planned, signers, cancellationToken).ConfigureAwait(false);
		return await SendCarbonTransactionAsync(Base16.Encode(bytes)).ConfigureAwait(false)
			?? throw new InvalidOperationException("sendCarbonTransaction returned no transaction hash");
	}

	/// <summary>Sends a message signed by one <see cref="ITxSigner"/>. See the list overload.</summary>
	public Task<string> SendTransactionAsync(TxMsg msg, ITxSigner signer, SendTransactionOptions? options = null, CancellationToken cancellationToken = default) =>
		SendTransactionAsync(msg, new[] { signer ?? throw new ArgumentNullException(nameof(signer)) }, options, cancellationToken);

	/// <summary>Sends a message signed by in-memory keys, one key per witness the message needs. See the list overload.</summary>
	public Task<string> SendTransactionAsync(TxMsg msg, IKeyPair[] keys, SendTransactionOptions? options = null, CancellationToken cancellationToken = default)
	{
		if (keys == null) throw new ArgumentNullException(nameof(keys));
		var signers = new ITxSigner[keys.Length];
		for (var i = 0; i < keys.Length; i++)
			signers[i] = new KeyPairSigner(keys[i]);
		return SendTransactionAsync(msg, signers, options, cancellationToken);
	}

	/// <summary>Sends a message signed by one in-memory key pair. See the list overload.</summary>
	public Task<string> SendTransactionAsync(TxMsg msg, IKeyPair keys, SendTransactionOptions? options = null, CancellationToken cancellationToken = default) =>
		SendTransactionAsync(msg, new ITxSigner[] { new KeyPairSigner(keys ?? throw new ArgumentNullException(nameof(keys))) }, options, cancellationToken);

	#endregion

	#region Storage

	/// <summary>
	/// Gets archive metadata by its hash
	/// </summary>
	/// <remarks>Warning: this Phantasma RPC method is currently stubbed and returns a default archive result until backend support is completed.</remarks>
	/// <param name="hash">Archive hash</param>
	/// <returns>Archive data or null</returns>
	public Task<ArchiveResult?> GetArchiveAsync(string hash) =>
		_rpc.SendRpcAsync<ArchiveResult>(Host, "getArchive", hash);

	/// <summary>
	/// Writes a single archive block
	/// </summary>
	/// <remarks>Warning: this Phantasma RPC method is currently stubbed and returns false; archive writes are not persisted until backend support is completed.</remarks>
	/// <param name="hash">Archive hash</param>
	/// <param name="blockIndex">Block index</param>
	/// <param name="blockContent">Block content bytes</param>
	/// <returns>True if the write succeeded</returns>
	public async Task<bool> WriteArchiveAsync(string hash, int blockIndex, byte[] blockContent)
	{
		var s = await _rpc.SendRpcAsync<string>(Host, "writeArchive", hash, blockIndex,
			Convert.ToBase64String(blockContent ?? Array.Empty<byte>()));
		return bool.TryParse(s, out var ok) && ok;
	}

	/// <summary>
	/// Reads a single archive block as a base64 string
	/// </summary>
	/// <remarks>Warning: this Phantasma RPC method is currently stubbed and returns an empty string until backend support is completed.</remarks>
	/// <param name="hash">Archive hash</param>
	/// <param name="blockIndex">Block index</param>
	/// <returns>Base64 block content or null</returns>
	public Task<string?> ReadArchiveAsync(string hash, int blockIndex) =>
		_rpc.SendRpcAsync<string>(Host, "readArchive", hash, blockIndex);

	#endregion

	#region Validation helpers

	/// <summary>
	/// Validates a WIF-formatted private key string
	/// </summary>
	/// <param name="key">WIF string</param>
	/// <returns>True if the key format looks valid</returns>
	public static bool IsValidPrivateKey(string key)
	{
		if (string.IsNullOrEmpty(key)) return false;
		return (key.StartsWith("L", false, CultureInfo.InvariantCulture) ||
				key.StartsWith("K", false, CultureInfo.InvariantCulture)) && key.Length == 52;
	}

	#endregion
}

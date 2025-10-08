using System.Globalization;
using System.Text;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.RPC.Types;

namespace PhantasmaPhoenix.RPC;

/// <summary>
/// Plain .NET RPC wrapper for Phantasma API using RpcClient with async and await
/// </summary>
public class PhantasmaAPI : IDisposable
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
	public PhantasmaAPI(string host, RpcClient? rpcClient)
	{
		Host = host ?? throw new ArgumentNullException(nameof(host));

		if (rpcClient == null)
		{
			_rpc = new RpcClient();
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
	/// Gets account information, including balances, for the specified address
	/// </summary>
	/// <param name="address">Account address</param>
	/// <returns>Account data or null if not found</returns>
	public Task<AccountResult?> GetAccountAsync(string address) =>
		_rpc.SendRpcAsync<AccountResult>(Host, "getAccount", address);

	/// <summary>
	/// Gets account information for multiple addresses
	/// </summary>
	/// <param name="addresses">Array of account addresses</param>
	/// <returns>Array of account results or null</returns>
	public Task<AccountResult[]?> GetAccountsAsync(string[] addresses) =>
		_rpc.SendRpcAsync<AccountResult[]>(Host, "getAccounts", string.Join(",", addresses ?? Array.Empty<string>()));

	/// <summary>
	/// Looks up an address by name
	/// </summary>
	/// <param name="name">Registered name to resolve</param>
	/// <returns>Resolved address text or null</returns>
	public Task<string?> LookUpNameAsync(string name) =>
		_rpc.SendRpcAsync<string>(Host, "lookUpName", name);

	#endregion

	#region Auction

	/// <summary>
	/// Gets the number of auctions currently available in the market contract for a given token
	/// <para><b>⚠️ Currently disabled - this functionality is not available and will be re-enabled according to the roadmap: https://phantasma.info/blockchain#roadmap</b></para>
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
	/// <para><b>⚠️ Currently disabled - this functionality is not available and will be re-enabled according to the roadmap: https://phantasma.info/blockchain#roadmap</b></para>
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
	/// <para><b>⚠️ Currently disabled - this functionality is not available and will be re-enabled according to the roadmap: https://phantasma.info/blockchain#roadmap</b></para>
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
	/// <param name="blockHash">Block hash</param>
	/// <returns>Transaction count</returns>
	public async Task<int> GetBlockTransactionCountByHashAsync(string blockHash)
	{
		var s = await _rpc.SendRpcAsync<string>(Host, "getBlockTransactionCountByHash", blockHash);
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
	/// Gets a transaction by block hash and transaction index
	/// </summary>
	/// <param name="blockHash">Block hash</param>
	/// <param name="index">Transaction index within the block</param>
	/// <returns>Transaction data or null</returns>
	public Task<TransactionResult?> GetTransactionByBlockHashAndIndexAsync(string blockHash, int index) =>
		_rpc.SendRpcAsync<TransactionResult>(Host, "getTransactionByBlockHashAndIndex", blockHash, index);

	#endregion

	#region Chain

	/// <summary>
	/// Gets an array of all chains deployed on Phantasma
	/// <para><b>⚠️ Currently disabled - this functionality is not available and will be re-enabled according to the roadmap: https://phantasma.info/blockchain#roadmap</b></para>
	/// </summary>
	/// <returns>Array of chains or null</returns>
	public Task<ChainResult[]?> GetChainsAsync() =>
		_rpc.SendRpcAsync<ChainResult[]>(Host, "getChains");

	#endregion

	#region Contract

	/// <summary>
	/// Gets contract metadata by name from the main chain
	/// <para><b>⚠️ Currently disabled - this functionality is not available and will be re-enabled according to the roadmap: https://phantasma.info/blockchain#roadmap</b></para>
	/// </summary>
	/// <param name="contractName">Contract name</param>
	/// <returns>Contract data or null</returns>
	public Task<ContractResult?> GetContractAsync(string contractName) =>
		_rpc.SendRpcAsync<ContractResult>(Host, "getContract", PhantasmaPhoenix.Protocol.DomainSettings.RootChainName, contractName);

	/// <summary>
	/// Gets all contracts deployed on the main chain
	/// <para><b>⚠️ Currently disabled - this functionality is not available and will be re-enabled according to the roadmap: https://phantasma.info/blockchain#roadmap</b></para>
	/// </summary>
	/// <returns>Array of contracts or null</returns>
	public Task<ContractResult[]?> GetContractsAsync() =>
		_rpc.SendRpcAsync<ContractResult[]>(Host, "getContracts", PhantasmaPhoenix.Protocol.DomainSettings.RootChainName);

	#endregion

	#region Leaderboard

	/// <summary>
	/// Gets a leaderboard by name
	/// <para><b>⚠️ Currently disabled - this functionality is not available and will be re-enabled according to the roadmap: https://phantasma.info/blockchain#roadmap</b></para>
	/// </summary>
	/// <param name="name">Leaderboard name</param>
	/// <returns>Leaderboard data or null</returns>
	public Task<LeaderboardResult?> GetLeaderboardAsync(string name) =>
		_rpc.SendRpcAsync<LeaderboardResult>(Host, "getLeaderboard", name);

	#endregion

	#region Nexus

	/// <summary>
	/// Gets nexus metadata including an array of all chains deployed on Phantasma
	/// <para><b>⚠️ Currently disabled - this functionality is not available and will be re-enabled according to the roadmap: https://phantasma.info/blockchain#roadmap</b></para>
	/// </summary>
	/// <returns>Nexus data or null</returns>
	public Task<NexusResult?> GetNexusAsync() =>
		_rpc.SendRpcAsync<NexusResult>(Host, "getNexus");

	#endregion

	#region Organization

	/// <summary>
	/// Gets organization data by id
	/// <para><b>⚠️ Currently disabled - this functionality is not available and will be re-enabled according to the roadmap: https://phantasma.info/blockchain#roadmap</b></para>
	/// </summary>
	/// <param name="id">Organization id</param>
	/// <returns>Organization data or null</returns>
	public Task<OrganizationResult?> GetOrganizationAsync(string id) =>
		_rpc.SendRpcAsync<OrganizationResult>(Host, "getOrganization", id);

	/// <summary>
	/// Gets organization data by name
	/// <para><b>⚠️ Currently disabled - this functionality is not available and will be re-enabled according to the roadmap: https://phantasma.info/blockchain#roadmap</b></para>
	/// </summary>
	/// <param name="name">Organization name</param>
	/// <returns>Organization data or null</returns>
	public Task<OrganizationResult?> GetOrganizationByNameAsync(string name) =>
		_rpc.SendRpcAsync<OrganizationResult>(Host, "getOrganizationByName", name);

	/// <summary>
	/// Gets all organizations deployed on Phantasma
	/// <para><b>⚠️ Currently disabled - this functionality is not available and will be re-enabled according to the roadmap: https://phantasma.info/blockchain#roadmap</b></para>
	/// </summary>
	/// <returns>Array of organizations or null</returns>
	public Task<OrganizationResult[]?> GetOrganizationsAsync() =>
		_rpc.SendRpcAsync<OrganizationResult[]>(Host, "getOrganizations");

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
	/// Gets an array of all tokens deployed on Phantasma
	/// </summary>
	/// <returns>Array of token metadata or null</returns>
	public Task<TokenResult[]?> GetTokensAsync() =>
		_rpc.SendRpcAsync<TokenResult[]>(Host, "getTokens");

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
	/// Gets token data for a specific token id
	/// <para><b>⚠️ This functionality is only partially implemented - some features may be missing or incomplete. See the roadmap for planned updates: https://phantasma.info/blockchain#roadmap</b></para>
	/// </summary>
	/// <param name="symbol">Token symbol</param>
	/// <param name="tokenId">Token id</param>
	/// <returns>Token data or null</returns>
	public Task<TokenDataResult?> GetTokenDataAsync(string symbol, string tokenId) =>
		_rpc.SendRpcAsync<TokenDataResult>(Host, "getTokenData", symbol, tokenId);

	/// <summary>
	/// Gets NFT data and optionally loads properties
	/// <para><b>⚠️ This functionality is only partially implemented - some features may be missing or incomplete. See the roadmap for planned updates: https://phantasma.info/blockchain#roadmap</b></para>
	/// </summary>
	/// <param name="symbol">NFT symbol</param>
	/// <param name="tokenId">Token id</param>
	/// <param name="loadProperties">True to load properties</param>
	/// <returns>NFT data or null</returns>
	public Task<TokenDataResult?> GetNFTAsync(string symbol, string tokenId, bool loadProperties) =>
		_rpc.SendRpcAsync<TokenDataResult>(Host, "getNFT", symbol, tokenId, loadProperties);

	/// <summary>
	/// Gets NFT data for multiple token ids
	/// <para><b>⚠️ Currently disabled - this functionality is not available and will be re-enabled according to the roadmap: https://phantasma.info/blockchain#roadmap</b></para>
	/// </summary>
	/// <param name="symbol">NFT symbol</param>
	/// <param name="tokenIds">Array of token ids</param>
	/// <returns>Array of NFT data or null</returns>
	public Task<TokenDataResult[]?> GetNFTsAsync(string symbol, string[] tokenIds) =>
		_rpc.SendRpcAsync<TokenDataResult[]>(Host, "getNFTs", symbol, tokenIds);

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
	/// <para><b>⚠️ Currently disabled - this functionality is not available and will be re-enabled according to the roadmap: https://phantasma.info/blockchain#roadmap</b></para>
	/// </summary>
	/// <param name="chain">Chain name</param>
	/// <param name="scriptData">Hex encoded script bytes</param>
	/// <returns>Script invocation result or null</returns>
	public Task<ScriptResult?> InvokeRawScriptAsync(string chain, string scriptData) =>
		_rpc.SendRpcAsync<ScriptResult>(Host, "invokeRawScript", chain, scriptData);

	/// <summary>
	/// Builds, signs and broadcasts a transaction with a UTF8 payload
	/// </summary>
	/// <param name="keys">Key pair used to sign a transaction</param>
	/// <param name="nexus">Nexus name</param>
	/// <param name="script">Transaction script bytes</param>
	/// <param name="chain">Target chain name</param>
	/// <param name="payload">UTF8 payload string</param>
	/// <param name="customSignFunction">Optional custom signer that receives data, script and payload bytes</param>
	/// <returns>Transaction hash text or null</returns>
	/// <exception cref="Exception">Thrown when the hash returned by RPC does not match the locally computed hash</exception>
	public async Task<string?> SignAndSendTransactionAsync(
		IKeyPair keys, string nexus, byte[] script, string chain, string payload,
		Func<byte[], byte[], byte[], byte[]>? customSignFunction = null)
	{
		var bytes = string.IsNullOrEmpty(payload) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(payload);
		return await SignAndSendTransactionAsync(keys, nexus, script, chain, bytes, customSignFunction);
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
	/// <returns>Transaction hash text or null</returns>
	/// <exception cref="Exception">Thrown when the hash returned by RPC does not match the locally computed hash</exception>
	public async Task<string?> SignAndSendTransactionAsync(
		IKeyPair keys, string nexus, byte[] script, string chain, byte[] payload,
		Func<byte[], byte[], byte[], byte[]>? customSignFunction = null)
	{
		var tx = new Protocol.Transaction(
			nexus, chain, script, DateTime.UtcNow + TimeSpan.FromMinutes(20), payload ?? Array.Empty<byte>());

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

	#endregion

	#region Storage

	/// <summary>
	/// Gets archive metadata by its hash
	/// <para><b>⚠️ Currently disabled - this functionality is not available and will be re-enabled according to the roadmap: https://phantasma.info/blockchain#roadmap</b></para>
	/// </summary>
	/// <param name="hash">Archive hash</param>
	/// <returns>Archive data or null</returns>
	public Task<ArchiveResult?> GetArchiveAsync(string hash) =>
		_rpc.SendRpcAsync<ArchiveResult>(Host, "getArchive", hash);

	/// <summary>
	/// Writes a single archive block
	/// <para><b>⚠️ Currently disabled - this functionality is not available and will be re-enabled according to the roadmap: https://phantasma.info/blockchain#roadmap</b></para>
	/// </summary>
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
	/// <para><b>⚠️ Currently disabled - this functionality is not available and will be re-enabled according to the roadmap: https://phantasma.info/blockchain#roadmap</b></para>
	/// </summary>
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

	/// <summary>
	/// Validates the format of a Phantasma address string
	/// </summary>
	/// <param name="address">Address text</param>
	/// <returns>True if the address format looks valid</returns>
	public static bool IsValidAddress(string address)
	{
		if (string.IsNullOrEmpty(address)) return false;
		return address.StartsWith("P", false, CultureInfo.InvariantCulture) && address.Length == 45;
	}

	#endregion
}

using PhantasmaPhoenix.Cryptography;

namespace PhantasmaPhoenix.Protocol;

// The clean Phantasma Link v5 wallet contract. This is the ONLY surface the v5 dispatcher
// (WalletLinkV5) talks to - it does not inherit or reference the legacy v1-v4 operations.
// Every operation that can fail reports WHY through a structured LinkFailure, so the dispatcher
// maps intent (the user said no) and problems (bad tx, locked wallet) to distinct error codes
// without parsing free-text. The wallet (WalletConnector) implements this by delegating to the
// same internal consent/sign/broadcast logic the legacy path uses; any legacy-shaped string is
// translated to a LinkFailure inside the wallet, so no legacy detail ever reaches the protocol.

/// <summary>Why a v5 operation did not succeed. The dispatcher maps each to a v5 error code.</summary>
public enum LinkFailure
{
	/// <summary>The operation succeeded.</summary>
	None = 0,
	/// <summary>The user declined the request in the wallet UI. -> 4001</summary>
	UserRejected,
	/// <summary>The submitted transaction was malformed or could not be described. -> -32602</summary>
	InvalidTransaction,
	/// <summary>No account is unlocked in the wallet. -> 4900</summary>
	NotLoggedIn,
	/// <summary>The requested signature kind is not supported for this operation. -> 5003</summary>
	UnsupportedSignatureKind,
	/// <summary>An unexpected wallet-side error. -> -32603</summary>
	Internal,
}

/// <summary>The serialized transaction format the dApp asked the wallet to sign/broadcast (spec §9.4).</summary>
public enum LinkTxFormat
{
	/// <summary>Classic Phantasma <c>Transaction</c> ("script") -> SendRawTransaction.</summary>
	Script,
	/// <summary>Carbon <c>SignedTxMsg</c> ("carbon") -> SendCarbonTransaction.</summary>
	Carbon,
}

/// <summary>One token balance line as the wallet exposes it to a dApp.</summary>
public sealed class LinkBalance
{
	public string Symbol { get; set; } = "";
	public string Value { get; set; } = "";
	public int Decimals { get; set; }
	public string[] Ids { get; set; } = Array.Empty<string>();
}

/// <summary>A wallet account as exposed to a dApp over v5.</summary>
public sealed class LinkAccount
{
	public string Address { get; set; } = "";
	public string Name { get; set; } = "";
	public string Avatar { get; set; } = "";
	public LinkBalance[] Balances { get; set; } = Array.Empty<LinkBalance>();
}

/// <summary>Wallet identity + node info for <c>pha_getWalletInfo</c>.</summary>
public sealed class LinkWalletInfo
{
	public string Name { get; set; } = "";
	public string Version { get; set; } = "";
	public string Rpc { get; set; } = "";
}

/// <summary>Chain context for <c>pha_getChains</c>. The dispatcher derives chain ids from the nexus.</summary>
public sealed class LinkChains
{
	public string Nexus { get; set; } = "";
}

/// <summary>Outcome of the <c>pha_connect</c> handshake.</summary>
public sealed class LinkConnectResult
{
	public LinkFailure Failure { get; set; }
	public string? Message { get; set; }
	public LinkAccount? Account { get; set; }
	public string WalletName { get; set; } = "";
	public string WalletVersion { get; set; } = "";
	public string Nexus { get; set; } = "";

	public static LinkConnectResult Ok(LinkAccount account, string walletName, string walletVersion, string nexus) =>
		new LinkConnectResult { Account = account, WalletName = walletName, WalletVersion = walletVersion, Nexus = nexus };

	public static LinkConnectResult Fail(LinkFailure failure, string? message) =>
		new LinkConnectResult { Failure = failure, Message = message };
}

/// <summary>Outcome of <c>pha_getAccounts</c>.</summary>
public sealed class LinkAccountResult
{
	public LinkFailure Failure { get; set; }
	public string? Message { get; set; }
	public LinkAccount? Account { get; set; }

	public static LinkAccountResult Ok(LinkAccount account) => new LinkAccountResult { Account = account };
	public static LinkAccountResult Fail(LinkFailure failure, string? message) =>
		new LinkAccountResult { Failure = failure, Message = message };
}

/// <summary>Outcome of <c>pha_signMessage</c>: the RAW 64-byte Ed25519 detached signature
/// over <see cref="LinkSignMessage.BuildPayload"/> plus the random the wallet prepended.</summary>
public sealed class LinkSignMessageResult
{
	public LinkFailure Failure { get; private set; }
	public string? Message { get; private set; }
	public byte[]? Signature { get; private set; }
	public byte[]? Random { get; private set; }

	public static LinkSignMessageResult Ok(byte[] signature, byte[] random) =>
		new LinkSignMessageResult { Signature = signature, Random = random };
	public static LinkSignMessageResult Fail(LinkFailure failure, string? message) =>
		new LinkSignMessageResult { Failure = failure, Message = message };
}

/// <summary>Outcome of <c>pha_signTransaction</c>: the fully signed serialized transaction
/// (the dApp broadcasts it itself; the wallet does NOT).</summary>
public sealed class LinkSignTransactionResult
{
	public LinkFailure Failure { get; private set; }
	public string? Message { get; private set; }
	public byte[]? SignedTx { get; private set; }

	public static LinkSignTransactionResult Ok(byte[] signedTx) =>
		new LinkSignTransactionResult { SignedTx = signedTx };
	public static LinkSignTransactionResult Fail(LinkFailure failure, string? message) =>
		new LinkSignTransactionResult { Failure = failure, Message = message };
}

/// <summary>Outcome of <c>pha_sendTransaction</c>.</summary>
public sealed class LinkSendResult
{
	public LinkFailure Failure { get; set; }
	public string? Message { get; set; }
	public Hash Hash { get; set; } = Hash.Null;

	public static LinkSendResult Ok(Hash hash) => new LinkSendResult { Hash = hash };
	public static LinkSendResult Fail(LinkFailure failure, string? message) =>
		new LinkSendResult { Failure = failure, Message = message };
}

/// <summary>Outcome of <c>pha_invokeScript</c>.</summary>
public sealed class LinkInvokeResult
{
	public LinkFailure Failure { get; set; }
	public string? Message { get; set; }
	public string[] Results { get; set; } = Array.Empty<string>();

	public static LinkInvokeResult Ok(string[] results) => new LinkInvokeResult { Results = results };
	public static LinkInvokeResult Fail(LinkFailure failure, string? message) =>
		new LinkInvokeResult { Failure = failure, Message = message };
}

/// <summary>
/// The clean v5 wallet operations the dispatcher calls. Callback-based because a wallet marshals
/// user-consent prompts onto its UI thread before completing. NOT related to the legacy
/// <c>WalletLink</c> operation surface.
/// </summary>
public interface IWalletLinkV5Ops
{
	/// <summary>Current wallet readiness; the dispatcher rejects non-connect calls unless Ready.</summary>
	WalletStatus Status { get; }

	/// <summary>
	/// Run the connect handshake for <paramref name="dappName"/> under the dispatcher-issued
	/// <paramref name="sessionToken"/> (prompt the user, then gather account + wallet version + nexus).
	/// </summary>
	void Connect(string dappName, string sessionToken, Action<LinkConnectResult> done);

	/// <summary>Return the active account (no prompt).</summary>
	void GetAccount(Action<LinkAccountResult> done);

	/// <summary>Return the current chain context (no prompt).</summary>
	void GetChains(Action<LinkChains> done);

	/// <summary>Return wallet identity + node info (no prompt).</summary>
	void GetWalletInfo(Action<LinkWalletInfo> done);

	/// <summary>Sign and broadcast a serialized transaction; prompts the user for consent.</summary>
	void SendTransaction(byte[] serializedTx, LinkTxFormat format, SignatureKind kind, ProofOfWork pow, Action<LinkSendResult> done);

	/// <summary>Sign EXACTLY the spec §8 payload built from <paramref name="message"/> (the op
	/// itself runs <see cref="LinkSignMessage.BuildPayload"/> with a fresh CSPRNG random);
	/// prompts the user, showing <paramref name="display"/> when the dApp provided a hint.</summary>
	void SignMessage(byte[] message, string? display, Action<LinkSignMessageResult> done);

	/// <summary>Sign a serialized transaction WITHOUT broadcasting; prompts the user for
	/// consent with a human-readable description.</summary>
	void SignTransaction(byte[] serializedTx, LinkTxFormat format, SignatureKind kind, ProofOfWork pow, Action<LinkSignTransactionResult> done);

	/// <summary>
	/// Ask the user to approve a deeplink/relay pairing (spec §15): "pair with dApp X?".
	/// Approval hands the channel key to the endpoint, which persists the pairing.
	/// </summary>
	void ConfirmPairing(LinkPairingParams pairing, Action<bool> done);

	/// <summary>Run a read-only VM script through the wallet's node (no prompt).</summary>
	void InvokeScript(string chain, byte[] script, Action<LinkInvokeResult> done);
}

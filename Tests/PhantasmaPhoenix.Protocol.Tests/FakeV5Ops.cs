using PhantasmaPhoenix.Cryptography;

namespace PhantasmaPhoenix.Protocol.Tests;

/// <summary>
/// Deterministic in-memory implementation of the clean v5 wallet contract, shared by the
/// dispatcher and deeplink-endpoint tests. Switches drive every branch (rejection, deferred
/// consent, structured failures) without any Unity host.
/// </summary>
internal sealed class FakeV5Ops : IWalletLinkV5Ops
{
	public WalletStatus StatusValue = WalletStatus.Ready;
	public string AccountAddress = "P2KTest";

	// Connect controls.
	public bool RejectConnect;
	public LinkFailure FailConnect = LinkFailure.None;
	public string? FailConnectMessage;
	public bool DeferConnect;
	public Action<LinkConnectResult>? PendingConnect;
	public string? LastConnectToken;
	public string? LastConnectDapp;
	/// <summary>How many times the consent flow (Connect) ran - resume must NOT increase it.</summary>
	public int ConnectCalls;

	// Other-op controls.
	public LinkFailure FailAccount = LinkFailure.None;
	public LinkFailure FailSend = LinkFailure.None;
	public string? FailSendMessage;
	public LinkFailure FailInvoke = LinkFailure.None;
	public LinkTxFormat? SentFormat;
	public byte[]? SentTx;

	public WalletStatus Status => StatusValue;

	private LinkAccount Account() => new LinkAccount
	{
		Address = AccountAddress,
		Name = "tester",
		Avatar = "",
		Balances = new[]
		{
			new LinkBalance { Symbol = "SOUL", Value = "1", Decimals = 8 },
		},
	};

	public void Connect(string dappName, string sessionToken, Action<LinkConnectResult> done)
	{
		ConnectCalls++;
		LastConnectDapp = dappName;
		LastConnectToken = sessionToken;
		if (DeferConnect) { PendingConnect = done; return; }
		if (RejectConnect) { done(LinkConnectResult.Fail(LinkFailure.UserRejected, "rejected")); return; }
		if (FailConnect != LinkFailure.None) { done(LinkConnectResult.Fail(FailConnect, FailConnectMessage)); return; }
		done(LinkConnectResult.Ok(Account(), "FakeWallet", "9.9.9", "localnet"));
	}

	public void GetAccount(Action<LinkAccountResult> done)
	{
		if (FailAccount != LinkFailure.None) { done(LinkAccountResult.Fail(FailAccount, "account unavailable")); return; }
		done(LinkAccountResult.Ok(Account()));
	}

	public void GetChains(Action<LinkChains> done) => done(new LinkChains { Nexus = "localnet" });

	public void GetWalletInfo(Action<LinkWalletInfo> done) =>
		done(new LinkWalletInfo { Name = "FakeWallet", Version = "9.9.9", Rpc = "http://localhost:7077/rpc" });

	public void SendTransaction(byte[] serializedTx, LinkTxFormat format, SignatureKind kind, ProofOfWork pow, Action<LinkSendResult> done)
	{
		SentFormat = format;
		SentTx = serializedTx;
		if (FailSend != LinkFailure.None) { done(LinkSendResult.Fail(FailSend, FailSendMessage)); return; }
		var hashBytes = new byte[32];
		hashBytes[0] = 1;
		done(LinkSendResult.Ok(Hash.FromBytes(hashBytes)));
	}

	public void InvokeScript(string chain, byte[] script, Action<LinkInvokeResult> done)
	{
		if (FailInvoke != LinkFailure.None) { done(LinkInvokeResult.Fail(FailInvoke, "invoke failed")); return; }
		done(LinkInvokeResult.Ok(new[] { "AABB" }));
	}

	// Pairing controls.
	public bool RejectPairing;
	public LinkPairingParams? LastPairing;
	public int ConfirmPairingCalls;

	public void ConfirmPairing(LinkPairingParams pairing, Action<bool> done)
	{
		ConfirmPairingCalls++;
		LastPairing = pairing;
		done(!RejectPairing);
	}
}

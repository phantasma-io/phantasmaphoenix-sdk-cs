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
	/// <summary>The consent callbacks of the deferred connects, in the order they came.</summary>
	public readonly List<Action<LinkConnectResult>> PendingConnects = new List<Action<LinkConnectResult>>();
	public string? LastConnectDapp;
	/// <summary>The caller of the last prompting operation (connect or sign).</summary>
	public LinkCaller? LastCaller;
	/// <summary>How many times the consent flow (Connect) ran - resume must NOT increase it.</summary>
	public int ConnectCalls;
	/// <summary>The lifetime the user picks at a connect.</summary>
	public LinkSessionLifetime ConnectLifetime = LinkSessionLifetime.Always;

	// Other-op controls.
	public LinkFailure FailAccount = LinkFailure.None;
	public LinkFailure FailSend = LinkFailure.None;
	public string? FailSendMessage;
	public LinkFailure FailInvoke = LinkFailure.None;
	public LinkTxFormat? SentFormat;
	public byte[]? SentTx;

	public WalletStatus Status => StatusValue;
	public string? SelectedAddress => StatusValue == WalletStatus.Ready ? AccountAddress : null;

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

	/// <summary>Connect keeps its callback and throws, as faulty wallet code would.</summary>
	public bool ThrowOnConnect;

	public void Connect(LinkCaller caller, Action<LinkConnectResult> done)
	{
		ConnectCalls++;
		LastConnectDapp = caller.DappName;
		LastCaller = caller;
		if (ThrowOnConnect) { PendingConnects.Add(done); throw new InvalidOperationException("wallet failure"); }
		if (DeferConnect) { PendingConnects.Add(done); return; }
		if (RejectConnect) { done(LinkConnectResult.Fail(LinkFailure.UserRejected, "rejected")); return; }
		if (FailConnect != LinkFailure.None) { done(LinkConnectResult.Fail(FailConnect, FailConnectMessage)); return; }
		done(LinkConnectResult.Ok(Account(), "FakeWallet", "9.9.9", "localnet", ConnectLifetime));
	}

	public void GetAccount(Action<LinkAccountResult> done)
	{
		if (FailAccount != LinkFailure.None) { done(LinkAccountResult.Fail(FailAccount, "account unavailable")); return; }
		done(LinkAccountResult.Ok(Account()));
	}

	/// <summary>GetChains throws, as faulty wallet code would.</summary>
	public bool ThrowOnGetChains;
	/// <summary>GetChains calls its callback twice, as faulty wallet code would.</summary>
	public bool AnswerTwice;

	/// <summary>GetChains keeps its callback for the test to run later, as a wallet that asks its
	/// node would.</summary>
	public bool DeferChains;
	public readonly List<Action> PendingChains = new List<Action>();

	public void GetChains(Action<LinkChains> done)
	{
		if (ThrowOnGetChains) { throw new InvalidOperationException("wallet failure"); }
		if (DeferChains) { PendingChains.Add(() => done(new LinkChains { Nexus = "localnet" })); return; }
		done(new LinkChains { Nexus = "localnet" });
		if (AnswerTwice) { done(new LinkChains { Nexus = "localnet" }); }
	}

	public void GetWalletInfo(Action<LinkWalletInfo> done) =>
		done(new LinkWalletInfo { Name = "FakeWallet", Version = "9.9.9", Rpc = "http://localhost:7077/rpc" });

	public void SendTransaction(LinkCaller caller, byte[] serializedTx, LinkTxFormat format, SignatureKind kind, Action<LinkSendResult> done)
	{
		LastCaller = caller;
		SentFormat = format;
		SentTx = serializedTx;
		if (FailSend != LinkFailure.None) { done(LinkSendResult.Fail(FailSend, FailSendMessage)); return; }
		var hashBytes = new byte[32];
		hashBytes[0] = 1;
		done(LinkSendResult.Ok(Hash.FromBytes(hashBytes)));
	}

	/// <summary>Override for the invoke result rows (e.g. a huge payload for chunking tests).</summary>
	public byte[][]? InvokeResults;

	/// <summary>How many times the node was asked to run a script.</summary>
	public int InvokeCalls;

	public void InvokeScript(string chain, byte[] script, Action<LinkInvokeResult> done)
	{
		InvokeCalls++;
		if (FailInvoke != LinkFailure.None) { done(LinkInvokeResult.Fail(FailInvoke, "invoke failed")); return; }
		done(LinkInvokeResult.Ok(InvokeResults ?? new[] { new byte[] { 0xAA, 0xBB } }));
	}

	// Sign-operation controls.
	public LinkFailure FailSignMessage = LinkFailure.None;
	public byte[]? SignedMessage;
	public string? SignedDisplay;
	public LinkFailure FailSignTx = LinkFailure.None;
	public LinkTxFormat? SignTxFormat;
	public SignatureKind? SignTxKind;

	public void SignMessage(LinkCaller caller, byte[] message, string? display, Action<LinkSignMessageResult> done)
	{
		LastCaller = caller;
		SignedMessage = message;
		SignedDisplay = display;
		if (FailSignMessage != LinkFailure.None) { done(LinkSignMessageResult.Fail(FailSignMessage, "sign refused")); return; }
		// Deterministic stand-ins: a fixed random and a recognizable signature blob.
		var random = new byte[LinkSignMessage.RandomLength];
		random[0] = 7;
		var signature = new byte[64];
		signature[0] = 9;
		done(LinkSignMessageResult.Ok(signature, random));
	}

	public void SignTransaction(LinkCaller caller, byte[] serializedTx, LinkTxFormat format, SignatureKind kind, Action<LinkSignTransactionResult> done)
	{
		LastCaller = caller;
		SignTxFormat = format;
		SignTxKind = kind;
		if (FailSignTx != LinkFailure.None) { done(LinkSignTransactionResult.Fail(FailSignTx, "sign refused")); return; }
		// Echo a marker plus the input so tests can assert the bytes round-tripped.
		var signed = new byte[serializedTx.Length + 1];
		signed[0] = 0x5A;
		Buffer.BlockCopy(serializedTx, 0, signed, 1, serializedTx.Length);
		done(LinkSignTransactionResult.Ok(signed));
	}

	// Pairing controls.
	public bool RejectPairing;
	/// <summary>The lifetime the user picks at a pairing.</summary>
	public LinkSessionLifetime PairingLifetime = LinkSessionLifetime.Always;
	public LinkPairingParams? LastPairing;
	public int ConfirmPairingCalls;

	public void ConfirmPairing(LinkPairingParams pairing, Action<LinkPairingDecision> done)
	{
		ConfirmPairingCalls++;
		LastPairing = pairing;
		done(RejectPairing ? LinkPairingDecision.Decline() : LinkPairingDecision.Approve(PairingLifetime));
	}
}

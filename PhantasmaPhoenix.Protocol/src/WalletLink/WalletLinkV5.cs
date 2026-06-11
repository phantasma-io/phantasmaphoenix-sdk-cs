using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.Cryptography;

namespace PhantasmaPhoenix.Protocol;

/// <summary>
/// Phantasma Link v5 dispatcher: parses the JSON-RPC-2.0 envelope (spec §4), validates it, and
/// routes <c>pha_*</c> methods to the clean <see cref="IWalletLinkV5Ops"/> contract. It shares no
/// code path with the legacy v1-v4 string protocol (<see cref="WalletLink"/>) and carries no
/// legacy shapes: every wallet outcome arrives as a structured <see cref="LinkFailure"/>, which the
/// dispatcher maps to a v5 error code. Methods not yet implemented by the wallet are reported as
/// CapabilityNotSupported and are simply not advertised in the handshake.
/// </summary>
public class WalletLinkV5
{
	public const int ProtocolVersion = 5;

	// Error codes - mirror the SDK taxonomy (spec §10 / errors.ts) so dApps branch on the number.
	private const int ErrParse = -32700;
	private const int ErrInvalidRequest = -32600;
	private const int ErrMethodNotFound = -32601;
	private const int ErrInvalidParams = -32602;
	private const int ErrInternal = -32603;
	private const int ErrUserRejected = 4001;
	private const int ErrUnauthorized = 4100;
	private const int ErrDisconnected = 4900;
	private const int ErrCapabilityNotSupported = 5004;

	private readonly IWalletLinkV5Ops _ops;
	// Authorized sessions (spec §7): persistent when the host provides a durable store, so a
	// returning dApp resumes without a new prompt. Plaintext loopback uses the session id as the
	// bearer; encrypted transports (deeplink/relay) add the channel key on top (later phases).
	private readonly ILinkSessionStore _sessions;

	// Single-flight lock with an OWNER and a TAKEOVER timeout. A bare boolean deadlocks the
	// dispatcher forever when a consent prompt is abandoned (client timed out / went away and
	// the operation callback never fires). The owner key lets a LATE completion of an abandoned
	// request release only ITS OWN lock instead of clobbering a newer request's lock; the timeout
	// lets a new request take over a stale lock.
	private string? _pendingOwner;
	private DateTime _pendingSinceUtc;
	/// <summary>Seconds after which a new request may take over an unanswered pending request.</summary>
	public int PendingTakeoverSeconds { get; set; } = 150;

	public WalletLinkV5(IWalletLinkV5Ops ops, ILinkSessionStore? sessions = null)
	{
		_ops = ops;
		_sessions = sessions ?? new InMemoryLinkSessionStore();
	}

	/// <summary>
	/// Handle one incoming envelope string and deliver exactly one response string via
	/// <paramref name="respond"/>. The wallet host is expected to marshal this onto its UI thread
	/// (operations prompt the user); responses may arrive asynchronously from those prompts.
	/// </summary>
	public void HandleMessage(string message, Action<string> respond)
	{
		JObject root;
		try
		{
			root = JObject.Parse(message);
		}
		catch
		{
			respond(BuildError(null, ErrParse, "Message is not valid JSON"));
			return;
		}

		var id = root["id"];

		if ((int?)root["plv"] != ProtocolVersion)
		{
			respond(BuildError(id, ErrInvalidRequest, "Unsupported Phantasma Link protocol version"));
			return;
		}

		var method = (string?)root["method"];
		if (string.IsNullOrEmpty(method))
		{
			respond(BuildError(id, ErrInvalidRequest, "Missing method"));
			return;
		}

		var prms = root["params"] as JObject ?? new JObject();
		var session = (string?)root["session"];

		// Everything except connect requires a Ready wallet and a valid session.
		if (method != "pha_connect")
		{
			if (_ops.Status != WalletStatus.Ready)
			{
				respond(BuildError(id, ErrDisconnected, $"Wallet is {_ops.Status}"));
				return;
			}
			if (session == null || _sessions.Get(session) == null)
			{
				respond(BuildError(id, ErrUnauthorized, "Invalid or missing session"));
				return;
			}
		}

		// One user-facing request at a time, mirroring the legacy dispatcher, but with a takeover
		// timeout so an abandoned consent prompt cannot deadlock the dispatcher.
		if (_pendingOwner != null && (DateTime.UtcNow - _pendingSinceUtc).TotalSeconds < PendingTakeoverSeconds)
		{
			respond(BuildError(id, ErrInternal, "A previous request is still pending"));
			return;
		}
		// Owner key: the envelope id is unique per client request; suffix with ticks so even a
		// reused id cannot make a stale completion release the new lock.
		var owner = $"{(string?)id ?? "-"}#{DateTime.UtcNow.Ticks}";
		_pendingOwner = owner;
		_pendingSinceUtc = DateTime.UtcNow;
		Action<string> complete = (response) =>
		{
			// Release the lock only if this request still owns it (a taken-over request that
			// completes late must not clobber the current owner's lock).
			if (_pendingOwner == owner)
			{
				_pendingOwner = null;
			}
			respond(response);
		};

		switch (method)
		{
			case "pha_connect": HandleConnect(id, prms, complete); break;
			case "pha_disconnect": HandleDisconnect(id, session, complete); break;
			case "pha_getAccounts": HandleGetAccounts(id, complete); break;
			case "pha_getChains": HandleGetChains(id, complete); break;
			case "pha_getWalletInfo": HandleGetWalletInfo(id, complete); break;
			case "pha_sendTransaction": HandleSendTransaction(id, prms, complete); break;
			case "pha_invokeScript": HandleInvokeScript(id, prms, complete); break;

			// Defined in the protocol but not yet implemented by this wallet; the handshake does
			// not advertise them, so a capability-aware dApp will not call them.
			case "pha_signMessage":
			case "pha_signTransaction":
				complete(BuildError(id, ErrCapabilityNotSupported, $"{method} is not yet supported by this wallet"));
				break;

			default:
				complete(BuildError(id, ErrMethodNotFound, $"Unknown method: {method}"));
				break;
		}
	}

	#region Handlers
	private void HandleConnect(JToken? id, JObject prms, Action<string> respond)
	{
		var dapp = prms["dapp"] as JObject;
		var dappName = (string?)dapp?["name"];
		// `is null || Length == 0` (rather than string.IsNullOrEmpty) so the compiler narrows
		// `dappName` to a non-null string for the calls below.
		if (dappName is null || dappName.Length == 0)
		{
			respond(BuildError(id, ErrInvalidParams, "Missing dapp.name"));
			return;
		}

		// Resume (spec §7): a dApp presenting a known session id for the SAME dApp identity gets
		// reconnected without a prompt - but only if the wallet still serves the same account.
		// Any mismatch falls back to the full consent flow below (one round-trip, fresh session).
		var resumeId = (string?)prms["session"];
		if (resumeId != null)
		{
			var record = _sessions.Get(resumeId);
			if (record != null && record.Dapp == dappName && _ops.Status == WalletStatus.Ready)
			{
				TryResume(id, dappName, record, respond);
				return;
			}
		}

		PromptConnect(id, dappName, respond);
	}

	/// <summary>Promptless reconnect for an already-authorized session.</summary>
	private void TryResume(JToken? id, string dappName, LinkSessionRecord record, Action<string> respond)
	{
		_ops.GetAccount(result =>
		{
			if (result.Failure != LinkFailure.None || result.Account == null || result.Account.Address != record.Address)
			{
				// Session no longer matches the wallet state (e.g. account switched): re-consent.
				PromptConnect(id, dappName, respond);
				return;
			}

			var account = result.Account;
			_ops.GetWalletInfo(info =>
				_ops.GetChains(chains =>
				{
					record.LastSeenUtc = DateTime.UtcNow;
					_sessions.Save(record);
					respond(BuildResult(id, BuildConnectResponse(record.Id, info.Name, info.Version, chains.Nexus, account)));
				}));
		});
	}

	/// <summary>The full consent flow: prompt the user, then persist a fresh session.</summary>
	private void PromptConnect(JToken? id, string dappName, Action<string> respond)
	{
		var token = Guid.NewGuid().ToString("N");
		_ops.Connect(dappName, token, result =>
		{
			if (result.Failure != LinkFailure.None || result.Account == null)
			{
				respond(BuildError(id, MapFailure(result.Failure), result.Message ?? "Connect failed"));
				return;
			}

			var now = DateTime.UtcNow;
			_sessions.Save(new LinkSessionRecord
			{
				Id = token,
				Dapp = dappName,
				Address = result.Account.Address,
				CreatedUtc = now,
				LastSeenUtc = now,
			});
			respond(BuildResult(id, BuildConnectResponse(token, result.WalletName, result.WalletVersion, result.Nexus, result.Account)));
		});
	}

	private static JObject BuildConnectResponse(string sessionId, string walletName, string walletVersion, string nexus, LinkAccount account)
	{
		return new JObject
		{
			["wallet"] = new JObject { ["name"] = walletName, ["version"] = walletVersion },
			["capabilities"] = BuildCapabilities(nexus),
			["account"] = BuildAccount(account),
			["session"] = new JObject { ["id"] = sessionId },
		};
	}

	private void HandleDisconnect(JToken? id, string? session, Action<string> respond)
	{
		if (session != null)
		{
			_sessions.Remove(session); // idempotent: removing an unknown session still succeeds
		}
		respond(BuildResult(id, new JObject { ["ok"] = true }));
	}

	private void HandleGetAccounts(JToken? id, Action<string> respond)
	{
		_ops.GetAccount(result =>
		{
			if (result.Failure != LinkFailure.None || result.Account == null)
			{
				respond(BuildError(id, MapFailure(result.Failure), result.Message ?? "Account unavailable"));
				return;
			}
			respond(BuildResult(id, new JObject { ["accounts"] = new JArray { BuildAccount(result.Account) } }));
		});
	}

	private void HandleGetChains(JToken? id, Action<string> respond)
	{
		_ops.GetChains(chains =>
		{
			var chain = "phantasma:" + chains.Nexus;
			respond(BuildResult(id, new JObject
			{
				["chains"] = new JArray { chain },
				["current"] = chain,
				["nexus"] = chains.Nexus,
			}));
		});
	}

	private void HandleGetWalletInfo(JToken? id, Action<string> respond)
	{
		_ops.GetWalletInfo(info =>
			respond(BuildResult(id, new JObject
			{
				["name"] = info.Name,
				["version"] = info.Version,
				["rpc"] = info.Rpc,
			})));
	}

	private void HandleSendTransaction(JToken? id, JObject prms, Action<string> respond)
	{
		var formatText = (string?)prms["format"];
		LinkTxFormat format;
		switch (formatText)
		{
			case "script": format = LinkTxFormat.Script; break;
			case "carbon": format = LinkTxFormat.Carbon; break;
			default:
				respond(BuildError(id, ErrInvalidParams, "params.format must be \"script\" or \"carbon\""));
				return;
		}

		byte[] txBytes;
		try
		{
			txBytes = Convert.FromBase64String((string?)prms["tx"] ?? "");
		}
		catch
		{
			respond(BuildError(id, ErrInvalidParams, "params.tx must be base64"));
			return;
		}
		if (txBytes.Length == 0)
		{
			respond(BuildError(id, ErrInvalidParams, "params.tx is empty"));
			return;
		}

		var kind = ParseSignatureKind(prms["signatureKind"]);
		var pow = ParseProofOfWork(prms["pow"]);

		_ops.SendTransaction(txBytes, format, kind, pow, result =>
		{
			if (result.Failure != LinkFailure.None || result.Hash == Hash.Null)
			{
				respond(BuildError(id, MapFailure(result.Failure), result.Message ?? "Transaction was not broadcast"));
				return;
			}
			respond(BuildResult(id, new JObject { ["hash"] = result.Hash.ToString() }));
		});
	}

	private void HandleInvokeScript(JToken? id, JObject prms, Action<string> respond)
	{
		var chain = (string?)prms["chain"] ?? "main";
		byte[] script;
		try
		{
			script = Convert.FromBase64String((string?)prms["script"] ?? "");
		}
		catch
		{
			respond(BuildError(id, ErrInvalidParams, "params.script must be base64"));
			return;
		}

		_ops.InvokeScript(chain, script, result =>
		{
			if (result.Failure != LinkFailure.None)
			{
				respond(BuildError(id, MapFailure(result.Failure), result.Message ?? "Invoke failed"));
				return;
			}
			respond(BuildResult(id, new JObject { ["results"] = new JArray(result.Results) }));
		});
	}
	#endregion

	#region Helpers
	private static int MapFailure(LinkFailure failure)
	{
		switch (failure)
		{
			case LinkFailure.UserRejected: return ErrUserRejected;
			case LinkFailure.InvalidTransaction: return ErrInvalidParams;
			case LinkFailure.NotLoggedIn: return ErrDisconnected;
			default: return ErrInternal;
		}
	}

	private static JObject BuildCapabilities(string nexus)
	{
		return new JObject
		{
			["plvVersions"] = new JArray { ProtocolVersion },
			["methods"] = new JArray
			{
				"pha_connect", "pha_disconnect", "pha_getAccounts", "pha_getChains",
				"pha_getWalletInfo", "pha_sendTransaction", "pha_invokeScript",
			},
			["chains"] = new JArray { "phantasma:" + nexus },
			// Only "carbon" is implemented end-to-end so far; "script" send is a follow-up.
			["txFormats"] = new JArray { "carbon" },
			["signatureKinds"] = new JArray { "Ed25519", "ECDSA" },
			["maxPayloadBytes"] = new JObject { ["loopback"] = 32 * 1024 * 1024 },
		};
	}

	private static JObject BuildAccount(LinkAccount account)
	{
		var balances = new JArray();
		foreach (var b in account.Balances)
		{
			var entry = new JObject
			{
				["symbol"] = b.Symbol,
				["value"] = b.Value,
				["decimals"] = b.Decimals,
			};
			if (b.Ids != null && b.Ids.Length > 0)
			{
				entry["ids"] = new JArray(b.Ids);
			}
			balances.Add(entry);
		}

		return new JObject
		{
			["address"] = account.Address,
			["name"] = account.Name,
			["avatar"] = account.Avatar,
			["balances"] = balances,
		};
	}

	private static SignatureKind ParseSignatureKind(JToken? token)
	{
		var value = (string?)token;
		if (!string.IsNullOrEmpty(value) && Enum.TryParse<SignatureKind>(value, true, out var kind))
		{
			return kind;
		}
		return SignatureKind.Ed25519;
	}

	private static ProofOfWork ParseProofOfWork(JToken? token)
	{
		var value = (string?)token;
		if (!string.IsNullOrEmpty(value) && Enum.TryParse<ProofOfWork>(value, true, out var pow))
		{
			return pow;
		}
		return ProofOfWork.None;
	}

	private static string BuildResult(JToken? id, JObject result)
	{
		return new JObject { ["plv"] = ProtocolVersion, ["id"] = id, ["result"] = result }.ToString(Formatting.None);
	}

	private static string BuildError(JToken? id, int code, string message)
	{
		return new JObject
		{
			["plv"] = ProtocolVersion,
			["id"] = id,
			["error"] = new JObject { ["code"] = code, ["message"] = message },
		}.ToString(Formatting.None);
	}
	#endregion
}

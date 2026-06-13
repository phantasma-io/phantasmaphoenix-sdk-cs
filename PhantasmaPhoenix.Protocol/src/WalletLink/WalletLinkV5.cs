using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.Cryptography;

namespace PhantasmaPhoenix.Protocol;

/// <summary>
/// Phantasma Link v5 dispatcher: parses the JSON-RPC-2.0 envelope (spec §4), validates it, and
/// routes <c>pha_*</c> methods to the clean <see cref="IWalletLinkV5Ops"/> contract. It shares no
/// code path with the legacy v1-v4 string protocol (<see cref="WalletLink"/>) and carries no
/// legacy shapes: every wallet outcome arrives as a structured <see cref="LinkFailure"/>, which the
/// dispatcher maps to a v5 error code. The full pha_* method surface is routed to the
/// contract; the capability handshake advertises exactly what the contract carries.
/// </summary>
public class WalletLinkV5
{
	public const int ProtocolVersion = 5;

	/// <summary>Unsolicited wallet->dApp event carrying a connect result right after a pairing
	/// approval (spec §17 step 3); lets the first connection complete in one user gesture.</summary>
	public const string SessionEstablishedEvent = "pha_sessionEstablished";

	// Error codes - mirror the SDK taxonomy (spec §10 / errors.ts) so dApps branch on the number.
	private const int ErrParse = -32700;
	private const int ErrInvalidRequest = -32600;
	private const int ErrMethodNotFound = -32601;
	private const int ErrInvalidParams = -32602;
	private const int ErrInternal = -32603;
	private const int ErrUserRejected = 4001;
	private const int ErrUnauthorized = 4100;
	private const int ErrDisconnected = 4900;
	private const int ErrPayloadTooLarge = 5001;
	private const int ErrUnsupportedSignatureKind = 5003;

	/// <summary>Per-transaction byte ceiling = the chain's max transaction size; the guard
	/// rejects oversized base64 BEFORE decoding or parsing (fast structured 5001 instead of
	/// an expensive parse failure).</summary>
	private const int MaxTxBytes = 32 * 1024 * 1024;
	private const long MaxTxBase64Chars = ((long)MaxTxBytes + 2) / 3 * 4;

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
			case "pha_signTransaction": HandleSignTransaction(id, prms, complete); break;
			case "pha_signMessage": HandleSignMessage(id, prms, complete); break;
			case "pha_invokeScript": HandleInvokeScript(id, prms, complete); break;

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

	/// <summary>
	/// Establish a session for an ALREADY-CONSENTED dApp and deliver the connect payload as an
	/// unsolicited <see cref="SessionEstablishedEvent"/> envelope (spec §17 step 3: on pairing
	/// approval the wallet publishes the encrypted pha_connect result). This method shows NO
	/// prompt - the caller must invoke it only from an explicit user approval whose consent text
	/// covers account access (the pairing consent). When the wallet is not Ready or has no
	/// account, it silently delivers nothing: the dApp then falls back to the classic explicit
	/// pha_connect (which prompts), so degradation is graceful, never an error.
	/// </summary>
	public void EstablishConsentedSession(string dappName, Action<string> deliver)
	{
		if (string.IsNullOrEmpty(dappName) || _ops.Status != WalletStatus.Ready)
		{
			return;
		}
		_ops.GetAccount(result =>
		{
			if (result.Failure != LinkFailure.None || result.Account == null)
			{
				return;
			}
			var account = result.Account;
			_ops.GetWalletInfo(info =>
				_ops.GetChains(chains =>
				{
					// Same record shape and token scheme as the prompted connect flow, so the
					// session is indistinguishable downstream (resume, disconnect, request auth).
					var token = Guid.NewGuid().ToString("N");
					var now = DateTime.UtcNow;
					_sessions.Save(new LinkSessionRecord
					{
						Id = token,
						Dapp = dappName,
						Address = account.Address,
						CreatedUtc = now,
						LastSeenUtc = now,
					});
					deliver(BuildEvent(SessionEstablishedEvent, token,
						BuildConnectResponse(token, info.Name, info.Version, chains.Nexus, account)));
				}));
		});
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

	/// <summary>Shared parsing for the two transaction methods: format + size-guarded base64
	/// tx + signature kind + pow. Returns false after responding with the structured error.</summary>
	private bool TryParseTxParams(JToken? id, JObject prms, Action<string> respond, out byte[] txBytes, out LinkTxFormat format, out SignatureKind kind, out ProofOfWork pow)
	{
		txBytes = Array.Empty<byte>();
		kind = SignatureKind.Ed25519;
		pow = ProofOfWork.None;
		format = LinkTxFormat.Carbon;

		switch ((string?)prms["format"])
		{
			case "script": format = LinkTxFormat.Script; break;
			case "carbon": format = LinkTxFormat.Carbon; break;
			default:
				respond(BuildError(id, ErrInvalidParams, "params.format must be \"script\" or \"carbon\""));
				return false;
		}

		var txText = (string?)prms["tx"] ?? "";
		// The pre-parse guard: refuse anything beyond the chain's max transaction size
		// before paying for base64 decode or deserialization (spec §10/§11: structured
		// 5001 carrying the limit, instead of a slow opaque parse failure).
		if (txText.Length > MaxTxBase64Chars)
		{
			respond(BuildError(id, ErrPayloadTooLarge, "Transaction exceeds the chain's maximum size",
				new JObject { ["maxPayloadBytes"] = MaxTxBytes }));
			return false;
		}
		try
		{
			txBytes = Convert.FromBase64String(txText);
		}
		catch
		{
			respond(BuildError(id, ErrInvalidParams, "params.tx must be base64"));
			return false;
		}
		if (txBytes.Length == 0)
		{
			respond(BuildError(id, ErrInvalidParams, "params.tx is empty"));
			return false;
		}

		kind = ParseSignatureKind(prms["signatureKind"]);
		pow = ParseProofOfWork(prms["pow"]);
		return true;
	}

	private void HandleSendTransaction(JToken? id, JObject prms, Action<string> respond)
	{
		if (!TryParseTxParams(id, prms, respond, out var txBytes, out var format, out var kind, out var pow))
		{
			return;
		}

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

	private void HandleSignTransaction(JToken? id, JObject prms, Action<string> respond)
	{
		if (!TryParseTxParams(id, prms, respond, out var txBytes, out var format, out var kind, out var pow))
		{
			return;
		}

		_ops.SignTransaction(txBytes, format, kind, pow, result =>
		{
			if (result.Failure != LinkFailure.None || result.SignedTx == null)
			{
				respond(BuildError(id, MapFailure(result.Failure), result.Message ?? "Transaction was not signed"));
				return;
			}
			respond(BuildResult(id, new JObject { ["signedTx"] = Convert.ToBase64String(result.SignedTx) }));
		});
	}

	private void HandleSignMessage(JToken? id, JObject prms, Action<string> respond)
	{
		var messageText = (string?)prms["message"] ?? "";
		if (messageText.Length == 0)
		{
			respond(BuildError(id, ErrInvalidParams, "params.message is required (base64)"));
			return;
		}
		// Same fast ceiling as transactions: a message has no business being larger than
		// the chain's max payload, and the guard keeps hostile input cheap to refuse.
		if (messageText.Length > MaxTxBase64Chars)
		{
			respond(BuildError(id, ErrPayloadTooLarge, "Message exceeds the maximum size",
				new JObject { ["maxPayloadBytes"] = MaxTxBytes }));
			return;
		}
		byte[] message;
		try
		{
			message = Convert.FromBase64String(messageText);
		}
		catch
		{
			respond(BuildError(id, ErrInvalidParams, "params.message must be base64"));
			return;
		}

		var display = (string?)prms["display"];
		_ops.SignMessage(message, display, result =>
		{
			if (result.Failure != LinkFailure.None || result.Signature == null || result.Random == null)
			{
				respond(BuildError(id, MapFailure(result.Failure), result.Message ?? "Message was not signed"));
				return;
			}
			respond(BuildResult(id, new JObject
			{
				["signature"] = Convert.ToBase64String(result.Signature),
				["random"] = Convert.ToBase64String(result.Random),
			}));
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
			case LinkFailure.UnsupportedSignatureKind: return ErrUnsupportedSignatureKind;
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
				"pha_getWalletInfo", "pha_signMessage", "pha_signTransaction",
				"pha_sendTransaction", "pha_invokeScript",
			},
			["chains"] = new JArray { "phantasma:" + nexus },
			["txFormats"] = new JArray { "script", "carbon" },
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

	private static string BuildError(JToken? id, int code, string message, JObject? data = null)
	{
		var error = new JObject { ["code"] = code, ["message"] = message };
		if (data != null)
		{
			error["data"] = data;
		}
		return new JObject
		{
			["plv"] = ProtocolVersion,
			["id"] = id,
			["error"] = error,
		}.ToString(Formatting.None);
	}

	// Event envelope (spec §4): discriminated by `type`, carries no request id. The session
	// rides both in the envelope and inside the data payload (the connect result), matching
	// what the TS client validates and dispatches.
	private static string BuildEvent(string eventName, string session, JObject data)
	{
		return new JObject
		{
			["plv"] = ProtocolVersion,
			["type"] = "event",
			["session"] = session,
			["event"] = eventName,
			["data"] = data,
		}.ToString(Formatting.None);
	}
	#endregion
}

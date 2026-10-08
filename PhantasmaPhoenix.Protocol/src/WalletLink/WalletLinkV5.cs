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
	/// approval (spec §15 step 3); lets the first connection complete in one user gesture.</summary>
	public const string SessionEstablishedEvent = "pha_sessionEstablished";

	/// <summary>Unsolicited wallet->dApp event telling the dApp its session ended (the wallet
	/// expired/evicted/revoked it, spec §7). The dApp should drop the session and re-pair rather
	/// than hang on requests the wallet will never answer.</summary>
	public const string SessionDeletedEvent = "pha_sessionDeleted";

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
	private const int ErrNexusMismatch = 5002;
	private const int ErrUnsupportedSignatureKind = 5003;

	/// <summary>The methods this dispatcher serves (spec §9.2), in the order the capabilities list
	/// them (spec §5).</summary>
	private static readonly string[] ServedMethods =
	{
		"pha_connect", "pha_disconnect", "pha_getAccounts", "pha_getChains", "pha_getWalletInfo",
		"pha_signMessage", "pha_signTransaction", "pha_sendTransaction", "pha_invokeScript",
	};

	/// <summary>Per-transaction byte ceiling = the chain's max transaction size; the guard
	/// rejects oversized base64 BEFORE decoding or parsing (fast structured 5001 instead of
	/// an expensive parse failure).</summary>
	internal const int MaxTxBytes = 32 * 1024 * 1024;
	internal const long MaxTxBase64Chars = ((long)MaxTxBytes + 2) / 3 * 4;

	/// <summary>The largest transaction this side accepts over a deeplink, in bytes (spec sections
	/// 6.3 and 11). On that path the transaction is encoded three times (base64 in the envelope,
	/// base64 in the sealed frame, base64url in the URL), and 3 KiB is what still fits the
	/// 8192-character URL budget. A larger request goes through the relay.</summary>
	private const int DeeplinkMaxPayloadBytes = 3072;

	private readonly IWalletLinkV5Ops _ops;
	// Authorized sessions (spec §7): persistent when the host provides a durable store, so a
	// returning dApp resumes without a new prompt. Plaintext loopback uses the session id as the
	// bearer; encrypted transports (deeplink/relay) add the channel key on top (later phases).
	private readonly ILinkSessionStore _sessions;

	public WalletLinkV5(IWalletLinkV5Ops ops, ILinkSessionStore? sessions = null)
	{
		_ops = ops;
		_sessions = sessions ?? new InMemoryLinkSessionStore();
	}

	/// <summary>
	/// Handle one incoming envelope string and deliver exactly one response string via
	/// <paramref name="respond"/>. The wallet host is expected to marshal this onto its UI thread
	/// (operations prompt the user); responses may arrive asynchronously from those prompts. The
	/// method never throws: a malformed request gets an error answer (spec §4).
	/// </summary>
	public void HandleMessage(string message, Action<string> respond)
	{
		// A wallet callback that fires twice, or an exception after the answer, must not send a
		// second answer.
		var answered = 0;
		Action<string> answer = response =>
		{
			if (System.Threading.Interlocked.Exchange(ref answered, 1) == 0)
			{
				respond(response);
			}
		};

		if (!TryReadRequest(message, out var request, out var refusal))
		{
			answer(refusal);
			return;
		}
		try
		{
			Dispatch(request, answer);
		}
		catch (Exception)
		{
			// Every request gets an answer, and no exception reaches the transport: a socket
			// callback that throws is taken for a dead connection.
			answer(BuildError(request.Id, ErrInternal, "Request failed"));
		}
	}

	/// <summary>A request envelope that passed the checks of spec §4.</summary>
	private sealed class RequestEnvelope
	{
		public JToken Id = JValue.CreateNull();
		public string Method = "";
		public string? Session;
		public JObject Params = new JObject();
	}

	/// <summary>
	/// Reads a request envelope (spec §4). Returns false with the error answer when the text is not
	/// JSON (-32700); when it is no JSON object, has another <c>plv</c>, no string <c>id</c>, no
	/// <c>method</c>, a <c>session</c> that is no string or a <c>sentAt</c> that is no number
	/// (-32600); or when <c>params</c> is no object (-32602). Fields it does not know are ignored.
	/// </summary>
	private static bool TryReadRequest(string message, out RequestEnvelope request, out string refusal)
	{
		request = new RequestEnvelope();
		refusal = "";
		JToken parsed;
		try
		{
			parsed = JToken.Parse(message);
		}
		catch (Exception)
		{
			refusal = BuildError(null, ErrParse, "Message is not valid JSON");
			return false;
		}
		if (!(parsed is JObject root))
		{
			refusal = BuildError(null, ErrInvalidRequest, "Message is not a JSON object");
			return false;
		}

		// The id goes into every later answer, so it is read first. Only a non-empty string can
		// correlate an answer; any other value is answered with a null id.
		var idToken = root["id"];
		var hasId = idToken != null && idToken.Type == JTokenType.String && ((string?)idToken)!.Length > 0;
		var id = hasId ? idToken! : JValue.CreateNull();

		var plv = root["plv"];
		if (plv == null || plv.Type != JTokenType.Integer || plv.ToString(Formatting.None) != ProtocolVersion.ToString())
		{
			refusal = BuildError(id, ErrInvalidRequest, "Unsupported Phantasma Link protocol version");
			return false;
		}
		if (!hasId)
		{
			refusal = BuildError(id, ErrInvalidRequest, "Request is missing a string id");
			return false;
		}
		var method = root["method"];
		if (method == null || method.Type != JTokenType.String || ((string?)method)!.Length == 0)
		{
			refusal = BuildError(id, ErrInvalidRequest, "Request is missing a method");
			return false;
		}
		var session = root["session"];
		if (session != null && session.Type != JTokenType.Null && session.Type != JTokenType.String)
		{
			refusal = BuildError(id, ErrInvalidRequest, "Request session must be a string");
			return false;
		}
		// Spec §4: a field of a request set to null counts as absent.
		var sentAt = root["sentAt"];
		if (sentAt != null && sentAt.Type != JTokenType.Null && sentAt.Type != JTokenType.Integer && sentAt.Type != JTokenType.Float)
		{
			refusal = BuildError(id, ErrInvalidRequest, "Request sentAt must be a number");
			return false;
		}
		var prms = root["params"];
		if (prms != null && prms.Type != JTokenType.Null && !(prms is JObject))
		{
			refusal = BuildError(id, ErrInvalidParams, "Request params must be an object");
			return false;
		}

		request.Id = id;
		request.Method = (string)method!;
		request.Session = session != null && session.Type == JTokenType.String ? (string?)session : null;
		request.Params = prms as JObject ?? new JObject();
		return true;
	}

	/// <summary>Routes a request that passed <see cref="TryReadRequest"/> to its handler.</summary>
	private void Dispatch(RequestEnvelope request, Action<string> respond)
	{
		var id = request.Id;
		var method = request.Method;
		var prms = request.Params;
		var session = request.Session;

		// Spec §4: a method that the wallet does not implement gets -32601 before any check of the
		// session or of the lock, because the methods of a wallet depend on neither.
		if (Array.IndexOf(ServedMethods, method) < 0)
		{
			respond(BuildError(id, ErrMethodNotFound, $"Unknown method: {method}"));
			return;
		}

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

		// Requests are independent (spec §4): each one is answered by its own id, whatever other
		// request still waits for the user. The wallet may queue its prompts.
		switch (method)
		{
			case "pha_connect": HandleConnect(id, prms, respond); break;
			case "pha_disconnect": HandleDisconnect(id, session, respond); break;
			case "pha_getAccounts": HandleGetAccounts(id, respond); break;
			case "pha_getChains": HandleGetChains(id, respond); break;
			case "pha_getWalletInfo": HandleGetWalletInfo(id, respond); break;
			case "pha_sendTransaction": HandleSendTransaction(id, prms, respond); break;
			case "pha_signTransaction": HandleSignTransaction(id, prms, respond); break;
			case "pha_signMessage": HandleSignMessage(id, prms, respond); break;
			case "pha_invokeScript": HandleInvokeScript(id, prms, respond); break;
		}
	}

	#region Handlers
	private void HandleConnect(JToken? id, JObject prms, Action<string> respond)
	{
		var dapp = prms["dapp"] as JObject;
		var dappName = TextOf(dapp?["name"]);
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
		var resumeId = TextOf(prms["session"]);
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
		// The dispatcher OWNS the session id: it mints the token, hands it to the dApp as the
		// session id, and persists the record below. The op only grants consent (no token passed in).
		var token = Guid.NewGuid().ToString("N");
		_ops.Connect(dappName, result =>
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
	/// unsolicited <see cref="SessionEstablishedEvent"/> envelope (spec §15 step 3: on pairing
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
		// Spec section 9.5: the wallet info carries the same capabilities object as the connect
		// result. Its chain list needs the current nexus.
		_ops.GetWalletInfo(info =>
			_ops.GetChains(chains =>
				respond(BuildResult(id, new JObject
				{
					["name"] = info.Name,
					["version"] = info.Version,
					["rpc"] = info.Rpc,
					["capabilities"] = BuildCapabilities(chains.Nexus),
				}))));
	}

	/// <summary>Shared parsing for the two transaction methods: format + size-guarded base64
	/// tx + signature kind + pow. Returns false after responding with the structured error.</summary>
	private bool TryParseTxParams(JToken? id, JObject prms, Action<string> respond, out byte[] txBytes, out LinkTxFormat format, out SignatureKind kind, out ProofOfWork pow)
	{
		txBytes = Array.Empty<byte>();
		kind = SignatureKind.Ed25519;
		pow = ProofOfWork.None;
		format = LinkTxFormat.Carbon;

		if (!TryReadText(prms, "format", out var formatText))
		{
			respond(BuildError(id, ErrInvalidParams, "params.format must be a string"));
			return false;
		}
		switch (formatText)
		{
			case "script": format = LinkTxFormat.Script; break;
			case "carbon": format = LinkTxFormat.Carbon; break;
			default:
				respond(BuildError(id, ErrInvalidParams, "params.format must be \"script\" or \"carbon\""));
				return false;
		}

		if (!TryReadText(prms, "tx", out var txValue))
		{
			respond(BuildError(id, ErrInvalidParams, "params.tx must be base64"));
			return false;
		}
		var txText = txValue ?? "";
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

		if (!TryReadText(prms, "signatureKind", out var kindText) || !TryReadText(prms, "pow", out var powText))
		{
			respond(BuildError(id, ErrInvalidParams, "params.signatureKind and params.pow must be strings"));
			return false;
		}
		kind = ParseSignatureKind(kindText);
		pow = ParseProofOfWork(powText);
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
		if (!TryReadText(prms, "message", out var messageValue) || !TryReadText(prms, "display", out var display))
		{
			respond(BuildError(id, ErrInvalidParams, "params.message and params.display must be strings"));
			return;
		}
		var messageText = messageValue ?? "";
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
		if (!TryReadText(prms, "chain", out var chainText) || !TryReadText(prms, "script", out var scriptText))
		{
			respond(BuildError(id, ErrInvalidParams, "params.chain and params.script must be strings"));
			return;
		}
		var chain = chainText ?? "main";
		byte[] script;
		try
		{
			script = Convert.FromBase64String(scriptText ?? "");
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
			case LinkFailure.NexusMismatch: return ErrNexusMismatch;
			default: return ErrInternal;
		}
	}

	private static JObject BuildCapabilities(string nexus)
	{
		return new JObject
		{
			["plvVersions"] = new JArray { ProtocolVersion },
			["methods"] = new JArray(ServedMethods),
			["chains"] = new JArray { "phantasma:" + nexus },
			["txFormats"] = new JArray { "script", "carbon" },
			["signatureKinds"] = new JArray { "Ed25519", "ECDSA" },
			// One size per transport this side serves (spec sections 5 and 11). The relay
			// chunking carries the chain maximum, like loopback.
			["maxPayloadBytes"] = new JObject
			{
				["loopback"] = MaxTxBytes,
				["relay"] = MaxTxBytes,
				["deeplink"] = DeeplinkMaxPayloadBytes,
			},
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

	private static SignatureKind ParseSignatureKind(string? value)
	{
		if (!string.IsNullOrEmpty(value) && Enum.TryParse<SignatureKind>(value, true, out var kind))
		{
			return kind;
		}
		return SignatureKind.Ed25519;
	}

	private static ProofOfWork ParseProofOfWork(string? value)
	{
		if (!string.IsNullOrEmpty(value) && Enum.TryParse<ProofOfWork>(value, true, out var pow))
		{
			return pow;
		}
		return ProofOfWork.None;
	}

	/// <summary>Returns the text of a JSON string, or null for a missing value or any other JSON
	/// type. A cast of another type would throw.</summary>
	private static string? TextOf(JToken? token)
	{
		return token != null && token.Type == JTokenType.String ? (string?)token : null;
	}

	/// <summary>Reads an optional string parameter into <paramref name="text"/>: null when it is
	/// missing or null. Returns false when it holds another JSON type, which the caller answers with
	/// -32602 (spec §4).</summary>
	private static bool TryReadText(JObject prms, string name, out string? text)
	{
		var token = prms[name];
		text = TextOf(token);
		return token == null || token.Type == JTokenType.Null || token.Type == JTokenType.String;
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

	/// <summary>Build the (unsealed) <see cref="SessionDeletedEvent"/> envelope for a session id.
	/// The relay client seals + publishes it as a best-effort notice when it evicts/expires a
	/// pairing (spec §7), so the dApp re-pairs instead of hanging.</summary>
	public string BuildSessionDeletedEnvelope(string sessionId)
	{
		return BuildEvent(SessionDeletedEvent, sessionId, new JObject());
	}

	/// <summary>Builds the (unsealed) error envelope that answers request <paramref name="id"/>
	/// with 5001: the response does not fit the transport. <c>data.maxPayloadBytes</c> carries
	/// the largest transaction the transport takes (spec section 10).</summary>
	internal string BuildPayloadTooLargeEnvelope(JToken id, int maxPayloadBytes)
	{
		return BuildError(id, ErrPayloadTooLarge, "Response exceeds the transport size limit",
			new JObject { ["maxPayloadBytes"] = maxPayloadBytes });
	}
	#endregion
}

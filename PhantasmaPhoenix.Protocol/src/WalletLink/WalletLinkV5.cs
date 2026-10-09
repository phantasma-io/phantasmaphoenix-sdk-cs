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

	/// <summary>Wallet->dApp event: eviction, the user's revoke or <c>pha_disconnect</c> ended the
	/// session (spec §7). Its data is <c>{ session }</c>. The dApp drops the session and pairs again
	/// rather than hang on requests the wallet will never answer.</summary>
	public const string SessionDeletedEvent = "pha_sessionDeleted";

	/// <summary>Wallet->dApp event: the lifetime of the session or the inactivity limit ended it
	/// (spec §7). Its data is <c>{ session }</c>.</summary>
	public const string SessionExpiredEvent = "pha_sessionExpired";

	/// <summary>Wallet->dApp event: the account of the session is selected again (spec §7, §9.5).
	/// Its data is <c>{ accounts }</c>, the session's account.</summary>
	public const string AccountsChangedEvent = "pha_accountsChanged";

	/// <summary>Wallet->dApp event: the wallet switched to another network (spec §9.5). Its data is
	/// <c>{ chain }</c>, the CAIP-2 id of the network.</summary>
	public const string ChainChangedEvent = "pha_chainChanged";

	// Error codes - mirror the SDK taxonomy (spec §10 / errors.ts) so dApps branch on the number.
	private const int ErrParse = -32700;
	private const int ErrInvalidRequest = -32600;
	private const int ErrMethodNotFound = -32601;
	private const int ErrInvalidParams = -32602;
	private const int ErrInternal = -32603;
	private const int ErrUserRejected = 4001;
	private const int ErrUnauthorized = 4100;
	private const int ErrDisconnected = 4900;
	private const int ErrUnsupportedChain = 4902;
	private const int ErrPayloadTooLarge = 5001;
	private const int ErrNexusMismatch = 5002;
	private const int ErrUnsupportedSignatureKind = 5003;
	private const int ErrCapabilityNotSupported = 5004;
	private const int ErrAccountNotSelected = 5006;
	private const int ErrSessionExpired = 5100;

	/// <summary>The methods this dispatcher serves (spec §9.2), in the order the capabilities list
	/// them (spec §5).</summary>
	private static readonly string[] ServedMethods =
	{
		"pha_connect", "pha_disconnect", "pha_getAccounts", "pha_getChains", "pha_getWalletInfo",
		"pha_signMessage", "pha_signTransaction", "pha_sendTransaction", "pha_invokeScript",
	};

	/// <summary>The largest serialized transaction a request may carry over loopback and the relay
	/// (spec §11): the chain's maximum. The guard rejects oversized base64 BEFORE decoding or
	/// parsing (fast structured 5001 instead of an expensive parse failure).</summary>
	public const int MaxTxBytes = 32 * 1024 * 1024;
	internal const long MaxTxBase64Chars = ((long)MaxTxBytes + 2) / 3 * 4;

	/// <summary>The largest transaction this side accepts over a deeplink, in bytes (spec sections
	/// 6.3 and 11). On that path the transaction is encoded three times (base64 in the envelope,
	/// base64 in the sealed frame, base64url in the URL), and 3 KiB is what still fits the
	/// 8192-character URL budget. A larger request goes through the relay.</summary>
	private const int DeeplinkMaxPayloadBytes = 3072;

	/// <summary>How long the wallet keeps the id of a request in a session (spec §18.2): at least
	/// 10 minutes.</summary>
	private static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(10);

	/// <summary>The most request ids a session keeps at once. A session that sends more within the
	/// replay window gets its new requests refused, because an id is never forgotten early
	/// (spec §18.2).</summary>
	private const int MaxSeenRequests = 1024;

	/// <summary>The longest request id (spec §4). The wallet keeps the ids it saw, so an id of any
	/// length would let a dApp fill its memory.</summary>
	private const int MaxIdChars = 128;

	private readonly IWalletLinkV5Ops _ops;
	// Authorized sessions (spec §7): persistent when the host provides a durable store, so a
	// returning dApp resumes without a new prompt. Loopback uses the session id as the bearer,
	// bound to the page origin; deeplink and relay requests also need the key of their pairing.
	private readonly ILinkSessionStore _sessions;
	private readonly WalletLinkV5Options _options;
	// The ids of the connects that prompt the user, by channel, with the end of their replay window
	// (spec §18.2). Such a connect has no session to keep its id, so its channel keeps it, while the
	// prompt is open and after the answer: a copy of it (a relay re-send) prompts no second time.
	private readonly Dictionary<string, Dictionary<string, DateTime>> _connectIds = new Dictionary<string, Dictionary<string, DateTime>>();

	public WalletLinkV5(IWalletLinkV5Ops ops, ILinkSessionStore? sessions = null, WalletLinkV5Options? options = null)
	{
		_ops = ops;
		_sessions = sessions ?? new InMemoryLinkSessionStore();
		_options = options ?? new WalletLinkV5Options();
	}

	/// <summary>Raised with a session id and the envelope of an event of that session (spec §4,
	/// §9.5). A transport delivers it on the session's channel, best effort.</summary>
	public event Action<string, string>? SessionEvent;

	/// <summary>Raised when a session ended (spec §7), after its notice went out through
	/// <see cref="SessionEvent"/>. A transport ends the channel of the session: a pairing ends with
	/// its session (spec §18.3).</summary>
	public event Action<string, LinkSessionEndReason>? SessionEnded;

	/// <summary>A session without a request for this long ends (spec §7).</summary>
	public TimeSpan SessionIdleTimeout => _options.SessionIdleTimeout;

	/// <summary>
	/// Handle one incoming envelope string and deliver at most one response string via
	/// <paramref name="respond"/>: a copy of a request that the session already handled gets
	/// none, because the first copy gets the answer (spec §18.2). The wallet host is expected to
	/// marshal this onto its UI thread (operations prompt the user); responses may arrive
	/// asynchronously from those prompts. The method never throws: a malformed request gets an
	/// error answer (spec §4).
	/// <paramref name="context"/> describes the channel; without it the request counts as a
	/// loopback request of unknown origin.
	/// </summary>
	public void HandleMessage(string message, Action<string> respond, LinkRequestContext? context = null)
	{
		context ??= new LinkRequestContext();
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
			Dispatch(request, context, answer);
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
		// Spec §4: a longer id is refused, and the answer does not repeat it.
		if (hasId && ((string?)idToken)!.Length > MaxIdChars)
		{
			refusal = BuildError(null, ErrInvalidRequest, $"Request id is longer than {MaxIdChars} characters");
			return false;
		}
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
	private void Dispatch(RequestEnvelope request, LinkRequestContext context, Action<string> respond)
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

		if (method == "pha_connect")
		{
			HandleConnect(id, session, prms, context, respond);
			return;
		}
		// Spec §9.5: pha_disconnect is idempotent, so an unknown or ended session gets its success
		// before the lock and session checks.
		if (method == "pha_disconnect")
		{
			HandleDisconnect(id, session, context, respond);
			return;
		}

		// Everything else requires a live session and a Ready wallet. The session comes first, so
		// that a copy of a request is dropped whatever the state of the wallet.
		var record = session == null ? null : _sessions.Get(session);
		if (record == null)
		{
			respond(BuildError(id, ErrUnauthorized, "Invalid or missing session"));
			return;
		}
		// Spec §7: a session is served only on the channel it was made on. This comes before the id
		// memory, so that another channel cannot fill it.
		if (!OnChannel(record, context))
		{
			respond(BuildError(id, ErrUnauthorized, "The session belongs to another channel"));
			return;
		}
		var now = DateTime.UtcNow;
		if (IsExpired(record, now))
		{
			// Spec §7: a request in an expired session ends it and gets 5100. Over deeplink and
			// relay the transport still holds the pairing key for this answer.
			EndSession(record.Id, LinkSessionEndReason.Expired);
			respond(BuildError(id, ErrSessionExpired, "Session expired"));
			return;
		}
		if (!Remember(record, id, now, respond))
		{
			return;
		}
		if (_ops.Status != WalletStatus.Ready)
		{
			respond(BuildError(id, ErrDisconnected, $"Wallet is {_ops.Status}"));
			return;
		}
		if (_ops.SelectedAddress != record.Address)
		{
			respond(AccountNotSelected(id, record));
			return;
		}
		context.OnSession?.Invoke(record.Id);
		var caller = CallerOf(record, context);

		// Requests are independent (spec §4): each one is answered by its own id, whatever other
		// request still waits for the user. The wallet may queue its prompts.
		switch (method)
		{
			case "pha_getAccounts": HandleGetAccounts(id, record, respond); break;
			case "pha_getChains": HandleGetChains(id, respond); break;
			case "pha_getWalletInfo": HandleGetWalletInfo(id, record, respond); break;
			case "pha_sendTransaction": HandleSendTransaction(id, prms, caller, respond); break;
			case "pha_signTransaction": HandleSignTransaction(id, prms, caller, respond); break;
			case "pha_signMessage": HandleSignMessage(id, prms, caller, respond); break;
			case "pha_invokeScript": HandleInvokeScript(id, prms, respond); break;
		}
	}

	/// <summary>Returns true when the request came on the channel of the session (spec §7). Over
	/// deeplink and relay the channel is the pairing, which carries one session, and a session made
	/// over a pairing is served there only. Over loopback it is the page origin: a session bound to
	/// an origin serves that origin only, never a caller without one, and a session made by a caller
	/// without an origin serves whoever names it.</summary>
	private static bool OnChannel(LinkSessionRecord record, LinkRequestContext context)
	{
		if (context.Transport == LinkTransportKind.Deeplink || context.Transport == LinkTransportKind.Relay)
		{
			return context.PairingSession == record.Id;
		}
		if (record.Transport == LinkTransportKind.Deeplink || record.Transport == LinkTransportKind.Relay)
		{
			return false;
		}
		return record.Origin == null || record.Origin == context.Origin;
	}

	/// <summary>
	/// Spec §18.2: the wallet handles each request id of a session once. Returns false when the
	/// request goes no further: a copy of a handled request is dropped without an answer, because
	/// the first copy gets it, and a session that holds the most ids the wallet keeps gets -32603.
	/// Otherwise the id is kept for the replay window with the session, so that the memory survives
	/// a restart, and the request moves the inactivity end of the session (spec §7).
	/// </summary>
	private bool Remember(LinkSessionRecord record, JToken id, DateTime now, Action<string> respond)
	{
		var requestId = (string)id!;
		record.SeenRequests ??= new List<LinkSeenRequest>();
		var pruned = record.SeenRequests.RemoveAll(seen => AsUtc(seen.UntilUtc) <= now) > 0;
		if (record.SeenRequests.Any(seen => seen.Id == requestId))
		{
			if (pruned)
			{
				_sessions.Save(record);
			}
			return false;
		}
		// An id is never forgotten early: a copy of its request would then be handled again.
		if (record.SeenRequests.Count >= MaxSeenRequests)
		{
			if (pruned)
			{
				_sessions.Save(record);
			}
			respond(BuildError(id, ErrInternal, "Too many requests in the session"));
			return false;
		}
		record.SeenRequests.Add(new LinkSeenRequest { Id = requestId, UntilUtc = now + ReplayWindow });
		record.LastSeenUtc = now;
		_sessions.Save(record);
		return true;
	}

	/// <summary>Returns the answer to a request in a session whose account is not the selected one
	/// (spec §7): 5006 with the session's account. The wallet answers and signs for a session with
	/// its account only, and the session stays, so that the dApp can ask the user to select that
	/// account and send the request again.</summary>
	private static string AccountNotSelected(JToken? id, LinkSessionRecord record)
	{
		return BuildError(id, ErrAccountNotSelected, "The account of the session is not the selected account",
			new JObject { ["address"] = record.Address });
	}

	/// <summary>Returns the caller of a request in the session <paramref name="record"/>: the
	/// approved dApp, the origin of this request, and the session's account.</summary>
	private static LinkCaller CallerOf(LinkSessionRecord record, LinkRequestContext context)
	{
		return new LinkCaller
		{
			DappName = record.Dapp,
			DappUrl = record.DappUrl,
			DappIcon = record.DappIcon,
			Origin = context.Origin,
			Address = record.Address,
			Transport = context.Transport,
		};
	}

	#region Sessions
	/// <summary>Ends a session (spec §7): forgets it, sends its dApp the notice that fits the
	/// reason, and has the transports end its channel. The host calls it when the user revokes a
	/// session. Returns false when the wallet did not keep the session; the notice and the end of
	/// the channel still go out, so a pairing that outlived its session ends too.</summary>
	public bool EndSession(string sessionId, LinkSessionEndReason reason)
	{
		if (string.IsNullOrEmpty(sessionId))
		{
			return false;
		}
		var known = _sessions.Get(sessionId) != null;
		_sessions.Remove(sessionId);
		var notice = reason == LinkSessionEndReason.Expired ? SessionExpiredEvent : SessionDeletedEvent;
		var envelope = BuildEvent(notice, sessionId, new JObject { ["session"] = sessionId });
		// Best effort (spec §7): a transport that fails must not keep the session alive or keep the
		// other transports from hearing of its end.
		Raise(SessionEvent, handler => handler(sessionId, envelope));
		Raise(SessionEnded, handler => handler(sessionId, reason));
		return known;
	}

	/// <summary>Ends every session with the lifetime <c>Session</c> (spec §7): that lifetime ends
	/// when the wallet locks, and a restart locks the wallet. The host calls it when the wallet
	/// locks, and at start once its transports are wired, so that the notices reach the dApps.</summary>
	public void EndSessionLifetimes()
	{
		foreach (var record in _sessions.List())
		{
			if (record.Lifetime == LinkSessionLifetime.Session)
			{
				EndSession(record.Id, LinkSessionEndReason.Expired);
			}
		}
	}

	/// <summary>Ends every session whose lifetime or inactivity limit passed (spec §7). A request
	/// in such a session ends it as well; this ends the sessions of dApps that stay quiet.</summary>
	public void ExpireSessions()
	{
		var now = DateTime.UtcNow;
		foreach (var record in _sessions.List())
		{
			if (IsExpired(record, now))
			{
				EndSession(record.Id, LinkSessionEndReason.Expired);
			}
		}
	}

	/// <summary>The host calls it when the user selects an account (spec §7): the live sessions of
	/// that account, which got 5006 while another account was selected, hear
	/// <c>pha_accountsChanged</c> with it.</summary>
	public void SelectedAccountChanged()
	{
		var address = _ops.SelectedAddress;
		if (string.IsNullOrEmpty(address) || _ops.Status != WalletStatus.Ready)
		{
			return;
		}
		var now = DateTime.UtcNow;
		var sessions = _sessions.List()
			.Where(record => record.Address == address && !IsExpired(record, now))
			.Select(record => record.Id)
			.ToArray();
		if (sessions.Length == 0)
		{
			return;
		}
		_ops.GetAccount(result =>
		{
			if (result.Failure != LinkFailure.None || result.Account == null || result.Account.Address != address)
			{
				return;
			}
			foreach (var session in sessions)
			{
				var envelope = BuildEvent(AccountsChangedEvent, session,
					new JObject { ["accounts"] = new JArray { BuildAccount(result.Account) } });
				Raise(SessionEvent, handler => handler(session, envelope));
			}
		});
	}

	/// <summary>The host calls it when the wallet switches to another network (spec §9.5): every
	/// live session hears <c>pha_chainChanged</c> with the CAIP-2 id of the network.</summary>
	public void ChainChanged()
	{
		_ops.GetChains(chains =>
		{
			var chain = ServedChains(chains.Nexus)[0];
			var now = DateTime.UtcNow;
			foreach (var record in _sessions.List().Where(record => !IsExpired(record, now)).ToArray())
			{
				var envelope = BuildEvent(ChainChangedEvent, record.Id, new JObject { ["chain"] = chain });
				Raise(SessionEvent, handler => handler(record.Id, envelope));
			}
		});
	}

	/// <summary>Returns true when the wallet keeps the session and it has not expired.</summary>
	public bool IsSessionLive(string sessionId)
	{
		var record = string.IsNullOrEmpty(sessionId) ? null : _sessions.Get(sessionId);
		return record != null && !IsExpired(record, DateTime.UtcNow);
	}

	private bool IsExpired(LinkSessionRecord record, DateTime now)
	{
		return (record.ExpiresUtc != null && now >= AsUtc(record.ExpiresUtc.Value))
			|| now - AsUtc(record.LastSeenUtc) >= _options.SessionIdleTimeout;
	}

	/// <summary>Returns a stored time as UTC. The dispatcher writes UTC only, but a store that
	/// serializes the record may read a time back without its kind.</summary>
	private static DateTime AsUtc(DateTime value)
	{
		switch (value.Kind)
		{
			case DateTimeKind.Utc: return value;
			case DateTimeKind.Local: return value.ToUniversalTime();
			default: return DateTime.SpecifyKind(value, DateTimeKind.Utc);
		}
	}

	/// <summary>Calls every handler of <paramref name="handlers"/>; a handler that throws does not
	/// stop the others.</summary>
	private static void Raise<T>(T? handlers, Action<T> invoke) where T : Delegate
	{
		if (handlers == null)
		{
			return;
		}
		foreach (var handler in handlers.GetInvocationList())
		{
			try
			{
				invoke((T)handler);
			}
			catch (Exception)
			{
				// Best effort, see the callers.
			}
		}
	}
	#endregion

	#region Handlers
	private void HandleConnect(JToken? id, string? session, JObject prms, LinkRequestContext context, Action<string> respond)
	{
		if (!TryReadDapp(prms, out var caller))
		{
			respond(BuildError(id, ErrInvalidParams, "params.dapp needs a name and a url"));
			return;
		}
		if (!TryReadNames(prms, "chains", out var chains) || !TryReadNames(prms, "methods", out var methods)
			|| !TryReadNames(prms, "features", out _))
		{
			respond(BuildError(id, ErrInvalidParams, "params.chains, params.methods and params.features must be arrays of strings"));
			return;
		}
		caller.Origin = context.Origin;
		caller.Transport = context.Transport;

		// Resume (spec §9.5): the session to resume rides in the envelope `session` field; over
		// deeplink and relay a connect without one resumes the session of its pairing. A live
		// session on its own channel (spec §7) reconnects without a prompt while the wallet serves
		// its account.
		var paired = context.Transport == LinkTransportKind.Deeplink || context.Transport == LinkTransportKind.Relay;
		if (paired && session != null && session != context.PairingSession)
		{
			// Spec §7: a pairing carries one session, and no request moves it to another one.
			respond(BuildError(id, ErrUnauthorized, "The session belongs to another channel"));
			return;
		}
		var resumeId = session ?? context.PairingSession;
		if (resumeId != null)
		{
			// Over deeplink and relay a pairing carries one session and ends with it: a resume
			// that fails there gets an error, never a second session (spec §9.5, §18.3). Over
			// loopback a session that ended or that the wallet does not know gets the full
			// approval. A live session is never replaced because the wallet is locked (spec §7):
			// its resume gets 4900 then, on every transport.
			var record = _sessions.Get(resumeId);
			// Spec §7: over loopback a session bound to an origin resumes for that origin only, a
			// session made without an origin only for the dApp name it was approved for, and a
			// session of a pairing never. A request from another channel neither ends nor resumes it.
			if (record != null && !paired
				&& (!OnChannel(record, context) || (record.Origin == null && record.Dapp != caller.DappName)))
			{
				respond(BuildError(id, ErrUnauthorized, "The session belongs to another channel"));
				return;
			}
			if (record != null && IsExpired(record, DateTime.UtcNow))
			{
				EndSession(record.Id, LinkSessionEndReason.Expired);
				if (paired)
				{
					respond(BuildError(id, ErrSessionExpired, "Session expired"));
					return;
				}
				record = null;
			}
			if (record != null)
			{
				if (!Remember(record, id!, DateTime.UtcNow, respond))
				{
					return;
				}
				if (_ops.Status == WalletStatus.Ready)
				{
					// Spec §7: the account rule applies to the resume too.
					if (_ops.SelectedAddress != record.Address)
					{
						respond(AccountNotSelected(id, record));
						return;
					}
					TryResume(id, record, context, respond);
					return;
				}
				respond(BuildError(id, ErrDisconnected, $"Wallet is {_ops.Status}"));
				return;
			}
			else if (paired)
			{
				respond(BuildError(id, ErrUnauthorized, "No valid session"));
				return;
			}
		}

		ConnectNew(id, caller, chains, methods, context, respond);
	}

	/// <summary>Reads an optional array of strings: the requested chains, methods or features of a
	/// connect (spec §5). Returns false when it holds another JSON type or a value that is no string.</summary>
	private static bool TryReadNames(JObject prms, string name, out string[] names)
	{
		names = Array.Empty<string>();
		var token = prms[name];
		if (token == null || token.Type == JTokenType.Null)
		{
			return true;
		}
		if (!(token is JArray array) || array.Any(item => item.Type != JTokenType.String))
		{
			return false;
		}
		names = array.Select(item => (string)item!).ToArray();
		return true;
	}

	/// <summary>Returns the networks the wallet serves: the one of its current nexus, as a CAIP-2
	/// id (spec §2, §5).</summary>
	private static string[] ServedChains(string nexus)
	{
		return new[] { "phantasma:" + nexus };
	}

	/// <summary>A new session (spec §5, §9.5): grants the requested networks and methods that the
	/// wallet serves, all of them when the dApp names none, then asks the user. This version
	/// defines no feature, so none is granted. A dApp that asks only for networks the wallet does
	/// not serve gets 4902 without a prompt (spec §10).</summary>
	private void ConnectNew(JToken? id, LinkCaller caller, string[] chains, string[] methods, LinkRequestContext context, Action<string> respond)
	{
		_ops.GetChains(current =>
		{
			var served = ServedChains(current.Nexus);
			var grantedChains = chains.Length == 0 ? served : chains.Where(served.Contains).Distinct().ToArray();
			if (grantedChains.Length == 0)
			{
				respond(BuildError(id, ErrUnsupportedChain, "The wallet serves none of the requested networks"));
				return;
			}
			// A copy: the record is the host's, and the served list must not change through it.
			var grantedMethods = methods.Length == 0 ? ServedMethods.ToArray() : methods.Where(ServedMethods.Contains).Distinct().ToArray();
			PromptConnect(id, caller, grantedChains, grantedMethods, context, respond);
		});
	}

	/// <summary>Reads the <c>dapp</c> param of a connect (spec §9.5): a non-empty <c>name</c> and
	/// <c>url</c>, and an optional <c>icon</c>, all strings. Returns false for anything else.</summary>
	private static bool TryReadDapp(JObject prms, out LinkCaller caller)
	{
		caller = new LinkCaller();
		if (!(prms["dapp"] is JObject dapp)
			|| !TryReadText(dapp, "name", out var name) || name is null || name.Length == 0
			|| !TryReadText(dapp, "url", out var url) || url is null || url.Length == 0
			|| !TryReadText(dapp, "icon", out var icon))
		{
			return false;
		}
		caller.DappName = name;
		caller.DappUrl = url;
		caller.DappIcon = icon ?? "";
		return true;
	}

	/// <summary>Promptless reconnect for an already-authorized session whose account is selected.
	/// The answer carries the capabilities granted to it.</summary>
	private void TryResume(JToken? id, LinkSessionRecord record, LinkRequestContext context, Action<string> respond)
	{
		_ops.GetAccount(result =>
		{
			if (result.Failure != LinkFailure.None || result.Account == null)
			{
				respond(BuildError(id, MapFailure(result.Failure), result.Message ?? "Account unavailable"));
				return;
			}
			if (result.Account.Address != record.Address)
			{
				respond(AccountNotSelected(id, record));
				return;
			}

			var account = result.Account;
			_ops.GetWalletInfo(info =>
				_ops.GetChains(chains =>
				{
					context.OnSession?.Invoke(record.Id);
					respond(BuildResult(id, BuildConnectResponse(record, info.Name, info.Version, chains.Nexus, account)));
				}));
		});
	}

	/// <summary>The full consent flow: prompt the user, then persist a fresh session bound to the
	/// dApp and the origin of the request, with the networks and methods granted to it and the
	/// lifetime the user picked.</summary>
	private void PromptConnect(JToken? id, LinkCaller caller, string[] grantedChains, string[] grantedMethods, LinkRequestContext context, Action<string> respond)
	{
		// A copy of a connect is dropped, while its prompt is open and after the answer: the first
		// copy gets the answer (spec §18.2).
		if (!RememberConnect(id, context, DateTime.UtcNow, respond))
		{
			return;
		}
		// A wallet callback that fires twice must not store a second session.
		var finished = 0;
		try
		{
			_ops.Connect(caller, result =>
			{
				if (System.Threading.Interlocked.Exchange(ref finished, 1) != 0)
				{
					return;
				}
				FinishConnect(id, caller, grantedChains, grantedMethods, context, respond, result);
			});
		}
		catch (Exception)
		{
			// The wallet's operation failed. This may run in a callback of the wallet, where no caller
			// answers for it: the dApp gets -32603 here, unless the operation answered before it threw,
			// and a late callback stores nothing. No prompt is open now, so the id keeps the window of
			// an answer.
			HoldConnect(id, context);
			if (System.Threading.Interlocked.Exchange(ref finished, 1) == 0)
			{
				respond(BuildError(id, ErrInternal, "Request failed"));
			}
		}
	}

	/// <summary>
	/// Spec §18.2: a connect that prompts the user has no session yet, so the wallet remembers its id
	/// with the channel it came on for the replay window: the page origin over loopback, or else the
	/// channel the transport names (a pairing, or a loopback connection without an origin). Returns
	/// false when the request goes no further: a copy is dropped without an answer, and a channel
	/// that holds the most ids the wallet keeps gets -32603.
	/// </summary>
	private bool RememberConnect(JToken? id, LinkRequestContext context, DateTime now, Action<string> respond)
	{
		var channel = ConnectChannel(context);
		lock (_connectIds)
		{
			foreach (var entry in _connectIds.ToArray())
			{
				foreach (var seen in entry.Value.Where(seen => seen.Value <= now).ToArray())
				{
					entry.Value.Remove(seen.Key);
				}
				if (entry.Value.Count == 0)
				{
					_connectIds.Remove(entry.Key);
				}
			}
			if (!_connectIds.TryGetValue(channel, out var ids))
			{
				ids = new Dictionary<string, DateTime>();
				_connectIds[channel] = ids;
			}
			var requestId = (string)id!;
			if (ids.ContainsKey(requestId))
			{
				return false;
			}
			if (ids.Count >= MaxSeenRequests)
			{
				respond(BuildError(id, ErrInternal, "Too many requests on the channel"));
				return false;
			}
			// Held while the prompt is open, however long; HoldConnect starts the window at the
			// answer.
			ids[requestId] = DateTime.MaxValue;
			return true;
		}
	}

	/// <summary>The channel whose connect ids a connect without a session joins (spec §18.2): the
	/// page origin over loopback, or else the channel the transport names.</summary>
	private static string ConnectChannel(LinkRequestContext context)
	{
		return context.Transport == LinkTransportKind.Loopback && context.Origin != null
			? "origin " + context.Origin
			: context.Channel ?? "";
	}

	/// <summary>Keeps the id of an answered connect for the replay window from the answer on, however
	/// long its prompt was open (spec §18.2).</summary>
	private void HoldConnect(JToken? id, LinkRequestContext context)
	{
		var channel = ConnectChannel(context);
		lock (_connectIds)
		{
			if (!_connectIds.TryGetValue(channel, out var ids))
			{
				ids = new Dictionary<string, DateTime>();
				_connectIds[channel] = ids;
			}
			ids[(string)id!] = DateTime.UtcNow + ReplayWindow;
		}
	}

	/// <summary>Stores the session of an approved connect and answers it.</summary>
	private void FinishConnect(JToken? id, LinkCaller caller, string[] grantedChains, string[] grantedMethods, LinkRequestContext context, Action<string> respond, LinkConnectResult result)
	{
		HoldConnect(id, context);
		if (result.Failure != LinkFailure.None || result.Account == null)
		{
			respond(BuildError(id, MapFailure(result.Failure), result.Message ?? "Connect failed"));
			return;
		}

		// The connect's id joins the ids of its session: over a pairing a copy of it resumes the
		// session, and is dropped there as a request the session already handled (spec §18.2).
		var record = SaveNewSession(caller, result.Account.Address, grantedChains, grantedMethods, result.Lifetime, (string)id!);
		context.OnSession?.Invoke(record.Id);
		respond(BuildResult(id, BuildConnectResponse(record, result.WalletName, result.WalletVersion, result.Nexus, result.Account)));
	}

	/// <summary>Stores a new session and keeps the store bounded: the expired sessions end first,
	/// then the least recently used ones past the limit (spec §7). The dispatcher OWNS the session
	/// id: it mints it here, and the ops only grant consent.</summary>
	private LinkSessionRecord SaveNewSession(LinkCaller caller, string address, string[] chains, string[] methods, LinkSessionLifetime lifetime, string? connectId = null)
	{
		ExpireSessions();
		var now = DateTime.UtcNow;
		var record = new LinkSessionRecord
		{
			Id = Guid.NewGuid().ToString("N"),
			Dapp = caller.DappName,
			DappUrl = caller.DappUrl,
			DappIcon = caller.DappIcon,
			Origin = caller.Origin,
			Transport = caller.Transport,
			Address = address,
			Chains = chains,
			Methods = methods,
			Features = Array.Empty<string>(),
			Lifetime = lifetime,
			ExpiresUtc = EndOf(lifetime, now),
			CreatedUtc = now,
			LastSeenUtc = now,
		};
		if (connectId != null)
		{
			record.SeenRequests.Add(new LinkSeenRequest { Id = connectId, UntilUtc = now + ReplayWindow });
		}
		_sessions.Save(record);
		EvictBeyondLimit(record.Id);
		return record;
	}

	/// <summary>Returns the fixed end of a lifetime that starts at <paramref name="now"/>, or null
	/// when it has none (spec §7).</summary>
	private static DateTime? EndOf(LinkSessionLifetime lifetime, DateTime now)
	{
		switch (lifetime)
		{
			case LinkSessionLifetime.OneHour: return now.AddHours(1);
			case LinkSessionLifetime.OneDay: return now.AddDays(1);
			case LinkSessionLifetime.OneMonth: return now.AddDays(30);
			default: return null;
		}
	}

	/// <summary>Keeps at most <see cref="WalletLinkV5Options.MaxSessions"/> sessions: the least
	/// recently used ones past it end as evicted (spec §7). The new session stays.</summary>
	private void EvictBeyondLimit(string newSessionId)
	{
		var records = _sessions.List();
		var extra = records.Length - Math.Max(1, _options.MaxSessions);
		if (extra <= 0)
		{
			return;
		}
		foreach (var record in records.Where(r => r.Id != newSessionId).OrderBy(r => AsUtc(r.LastSeenUtc)).Take(extra).ToArray())
		{
			EndSession(record.Id, LinkSessionEndReason.Evicted);
		}
	}

	/// <summary>Returns the <c>pha_connect</c> result of a session (spec §5): the wallet, the
	/// capabilities granted to the session, the account and the session with its end.</summary>
	private JObject BuildConnectResponse(LinkSessionRecord record, string walletName, string walletVersion, string nexus, LinkAccount account)
	{
		return new JObject
		{
			["wallet"] = new JObject { ["name"] = walletName, ["version"] = walletVersion },
			["capabilities"] = BuildCapabilities(record, nexus),
			["account"] = BuildAccount(account),
			// The end of the lifetime in Unix milliseconds, or null without a fixed end (spec §5).
			["session"] = new JObject
			{
				["id"] = record.Id,
				["expiresAt"] = record.ExpiresUtc == null
					? JValue.CreateNull()
					: new JValue(new DateTimeOffset(AsUtc(record.ExpiresUtc.Value)).ToUnixTimeMilliseconds()),
			},
		};
	}

	/// <summary>
	/// Establish a session for an ALREADY-CONSENTED dApp and deliver the connect payload as an
	/// unsolicited <see cref="SessionEstablishedEvent"/> envelope (spec §15 step 3: on pairing
	/// approval the wallet publishes the encrypted pha_connect result). This method shows NO
	/// prompt - the caller must invoke it only from an explicit user approval whose consent text
	/// covers account access (the pairing consent). When the wallet is not Ready or has no
	/// account, it silently delivers nothing: the dApp then falls back to the classic explicit
	/// pha_connect (which prompts), so degradation is graceful, never an error. The session is
	/// bound to the dApp and the origin of <paramref name="caller"/> and lasts the
	/// <paramref name="lifetime"/> the user picked at the pairing. The caller's
	/// <see cref="LinkCaller.Transport"/> names the pairing's transport, deeplink or relay: the
	/// session is then served over that pairing only (spec §7).
	/// </summary>
	public void EstablishConsentedSession(LinkCaller caller, LinkSessionLifetime lifetime, Action<string> deliver)
	{
		if (string.IsNullOrEmpty(caller.DappName) || _ops.Status != WalletStatus.Ready)
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
					// Same record as the prompted connect flow, so the session is
					// indistinguishable downstream (resume, disconnect, request auth). The pairing
					// asked for nothing in particular: the session gets every network and method
					// the wallet serves.
					var record = SaveNewSession(caller, account.Address, ServedChains(chains.Nexus), ServedMethods.ToArray(), lifetime);
					deliver(BuildEvent(SessionEstablishedEvent, record.Id,
						BuildConnectResponse(record, info.Name, info.Version, chains.Nexus, account)));
				}));
		});
	}

	/// <summary>
	/// Delivers the live session <paramref name="sessionId"/> again as an unsolicited
	/// <see cref="SessionEstablishedEvent"/> envelope (spec §15): a pairing link opened again opens no
	/// second session, and its dApp gets the session it may have missed. Delivers nothing when the
	/// session is not live, or the wallet is not Ready with the session's account selected.
	/// </summary>
	internal void PushSessionAgain(string sessionId, Action<string> deliver)
	{
		var record = _sessions.Get(sessionId);
		if (record == null || IsExpired(record, DateTime.UtcNow) || _ops.Status != WalletStatus.Ready
			|| _ops.SelectedAddress != record.Address)
		{
			return;
		}
		_ops.GetAccount(result =>
		{
			if (result.Failure != LinkFailure.None || result.Account == null || result.Account.Address != record.Address)
			{
				return;
			}
			var account = result.Account;
			_ops.GetWalletInfo(info =>
				_ops.GetChains(chains =>
					deliver(BuildEvent(SessionEstablishedEvent, record.Id,
						BuildConnectResponse(record, info.Name, info.Version, chains.Nexus, account)))));
		});
	}

	private void HandleDisconnect(JToken? id, string? session, LinkRequestContext context, Action<string> respond)
	{
		// A live session of another channel is left alone, and the request gets 4100: its id alone
		// does not end it (spec §7, §9.5). An ended one is a success no-op there; the sweep ends it
		// as expired. An unknown session still ends, so that a channel that outlived it ends too.
		var record = session == null ? null : _sessions.Get(session);
		if (record != null && !OnChannel(record, context))
		{
			respond(IsExpired(record, DateTime.UtcNow)
				? BuildResult(id, new JObject { ["ok"] = true })
				: BuildError(id, ErrUnauthorized, "The session belongs to another channel"));
			return;
		}
		if (session != null)
		{
			EndSession(session, LinkSessionEndReason.Disconnected);
		}
		respond(BuildResult(id, new JObject { ["ok"] = true }));
	}

	private void HandleGetAccounts(JToken? id, LinkSessionRecord record, Action<string> respond)
	{
		// Spec §9.5: only the account of this session.
		_ops.GetAccount(result =>
		{
			if (result.Failure != LinkFailure.None || result.Account == null)
			{
				respond(BuildError(id, MapFailure(result.Failure), result.Message ?? "Account unavailable"));
				return;
			}
			if (result.Account.Address != record.Address)
			{
				respond(AccountNotSelected(id, record));
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

	private void HandleGetWalletInfo(JToken? id, LinkSessionRecord record, Action<string> respond)
	{
		// Spec section 9.5: the wallet info carries the capabilities granted to this session, the
		// same object as in the connect result.
		_ops.GetWalletInfo(info =>
			_ops.GetChains(chains =>
				respond(BuildResult(id, new JObject
				{
					["name"] = info.Name,
					["version"] = info.Version,
					["rpc"] = info.Rpc,
					["capabilities"] = BuildCapabilities(record, chains.Nexus),
				}))));
	}

	/// <summary>Shared parsing for the two transaction methods: format + size-guarded base64
	/// tx + signature kind. Returns false after responding with the structured error.</summary>
	private bool TryParseTxParams(JToken? id, JObject prms, Action<string> respond, out byte[] txBytes, out LinkTxFormat format, out SignatureKind kind)
	{
		txBytes = Array.Empty<byte>();
		kind = SignatureKind.Ed25519;
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
			case null:
				respond(BuildError(id, ErrInvalidParams, "params.format is required"));
				return false;
			default:
				// Spec §9.4: a format that the wallet does not support gets 5004.
				respond(BuildError(id, ErrCapabilityNotSupported, $"Unsupported transaction format: {formatText}"));
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

		if (!TryReadText(prms, "signatureKind", out var kindText) || !TryParseSignatureKind(kindText, out kind))
		{
			respond(BuildError(id, ErrInvalidParams, "params.signatureKind must be \"Ed25519\" or \"ECDSA\""));
			return false;
		}
		// Spec §9.5: Carbon witnesses are Ed25519 only.
		if (format == LinkTxFormat.Carbon && kind != SignatureKind.Ed25519)
		{
			respond(BuildError(id, ErrUnsupportedSignatureKind, "A Carbon transaction is signed with the Ed25519 key only"));
			return false;
		}
		return true;
	}

	private void HandleSendTransaction(JToken? id, JObject prms, LinkCaller caller, Action<string> respond)
	{
		if (!TryParseTxParams(id, prms, respond, out var txBytes, out var format, out var kind))
		{
			return;
		}

		_ops.SendTransaction(caller, txBytes, format, kind, result =>
		{
			if (result.Failure != LinkFailure.None || result.Hash == Hash.Null)
			{
				respond(BuildError(id, MapFailure(result.Failure), result.Message ?? "Transaction was not broadcast"));
				return;
			}
			respond(BuildResult(id, new JObject { ["hash"] = result.Hash.ToString() }));
		});
	}

	private void HandleSignTransaction(JToken? id, JObject prms, LinkCaller caller, Action<string> respond)
	{
		if (!TryParseTxParams(id, prms, respond, out var txBytes, out var format, out var kind))
		{
			return;
		}

		_ops.SignTransaction(caller, txBytes, format, kind, result =>
		{
			if (result.Failure != LinkFailure.None || result.SignedTx == null)
			{
				respond(BuildError(id, MapFailure(result.Failure), result.Message ?? "Transaction was not signed"));
				return;
			}
			respond(BuildResult(id, new JObject { ["signedTx"] = Convert.ToBase64String(result.SignedTx) }));
		});
	}

	private void HandleSignMessage(JToken? id, JObject prms, LinkCaller caller, Action<string> respond)
	{
		if (!TryReadText(prms, "message", out var messageValue) || !TryReadText(prms, "display", out var display))
		{
			respond(BuildError(id, ErrInvalidParams, "params.message and params.display must be strings"));
			return;
		}
		// Spec §9.5: the wallet signs messages with the account's Ed25519 key only, and an unknown
		// kind is a bad param, as on every method (§9.7).
		if (!TryReadText(prms, "signatureKind", out var kindText) || !TryParseSignatureKind(kindText, out var kind))
		{
			respond(BuildError(id, ErrInvalidParams, "params.signatureKind must be \"Ed25519\" or \"ECDSA\""));
			return;
		}
		if (kind != SignatureKind.Ed25519)
		{
			respond(BuildError(id, ErrUnsupportedSignatureKind, "Messages are signed with the Ed25519 key only"));
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

		_ops.SignMessage(caller, message, display, result =>
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
		if (!TryReadText(prms, "chain", out var chain) || !TryReadText(prms, "script", out var scriptText))
		{
			respond(BuildError(id, ErrInvalidParams, "params.chain and params.script must be strings"));
			return;
		}
		// Spec §9.5: both are required. `chain` names a chain of the nexus, for example "main".
		if (chain is null || chain.Length == 0)
		{
			respond(BuildError(id, ErrInvalidParams, "params.chain is required"));
			return;
		}
		if (scriptText is null || scriptText.Length == 0)
		{
			respond(BuildError(id, ErrInvalidParams, "params.script is required (base64)"));
			return;
		}
		// The same ceiling as a transaction: the guard refuses hostile input before decoding it.
		if (scriptText.Length > MaxTxBase64Chars)
		{
			respond(BuildError(id, ErrPayloadTooLarge, "Script exceeds the maximum size",
				new JObject { ["maxPayloadBytes"] = MaxTxBytes }));
			return;
		}
		byte[] script;
		try
		{
			script = Convert.FromBase64String(scriptText);
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
			var results = new JArray();
			foreach (var item in result.Results)
			{
				results.Add(Convert.ToBase64String(item));
			}
			respond(BuildResult(id, new JObject { ["results"] = results }));
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

	/// <summary>Returns the capabilities granted to a session (spec §5, §9.5). A session stored
	/// before the grants were kept gets every network and method the wallet serves.</summary>
	private JObject BuildCapabilities(LinkSessionRecord record, string nexus)
	{
		// One size per transport the host serves (spec sections 5 and 11). The relay chunking
		// carries the chain maximum, like loopback.
		var maxPayloadBytes = new JObject();
		foreach (var transport in _options.ServedTransports)
		{
			switch (transport)
			{
				case LinkTransportKind.Loopback: maxPayloadBytes["loopback"] = MaxTxBytes; break;
				case LinkTransportKind.Relay: maxPayloadBytes["relay"] = MaxTxBytes; break;
				case LinkTransportKind.Deeplink: maxPayloadBytes["deeplink"] = DeeplinkMaxPayloadBytes; break;
			}
		}
		return new JObject
		{
			["plvVersions"] = new JArray { ProtocolVersion },
			["methods"] = new JArray(record.Methods ?? ServedMethods),
			["chains"] = new JArray(record.Chains ?? ServedChains(nexus)),
			["txFormats"] = new JArray { "script", "carbon" },
			["signatureKinds"] = new JArray { "Ed25519", "ECDSA" },
			// This version defines no feature value, so the list is empty (spec §5).
			["features"] = new JArray(),
			["maxPayloadBytes"] = maxPayloadBytes,
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

	/// <summary>Reads a <c>signatureKind</c> param (spec §9.7): a missing one means Ed25519, and only
	/// the exact names "Ed25519" and "ECDSA" are kinds. Returns false for any other value.</summary>
	private static bool TryParseSignatureKind(string? value, out SignatureKind kind)
	{
		switch (value)
		{
			case null:
			case "Ed25519":
				kind = SignatureKind.Ed25519;
				return true;
			case "ECDSA":
				kind = SignatureKind.ECDSA;
				return true;
			default:
				kind = SignatureKind.None;
				return false;
		}
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

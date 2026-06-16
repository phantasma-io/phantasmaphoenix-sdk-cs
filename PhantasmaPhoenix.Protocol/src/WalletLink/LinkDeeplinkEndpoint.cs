using System.Text;

namespace PhantasmaPhoenix.Protocol;

/// <summary>
/// Wallet-side deeplink endpoint for Phantasma Link v5 (spec §17). The host feeds it every URL
/// the OS delivers (Android intent / iOS universal link / editor harness); it consumes the v5
/// ones: pairing URIs establish an encrypted channel (after user consent via
/// <see cref="IWalletLinkV5Ops.ConfirmPairing"/>), request URLs are unsealed with the pairing
/// key, dispatched through <see cref="WalletLinkV5"/>, and answered by opening the dApp's
/// callback URL with the sealed response. Frames on this transport are ALWAYS encrypted;
/// anything that does not authenticate is dropped without a reply (answering plaintext to an
/// unauthenticated sender would leak).
/// </summary>
public sealed class LinkDeeplinkEndpoint
{
	private readonly WalletLinkV5 _dispatcher;
	private readonly IWalletLinkV5Ops _ops;
	private readonly ILinkPairingStore _pairings;
	private readonly LinkRelayClient? _relay;
	// Lifecycle log (mirrors LinkRelayClient's): without it the deeplink path is a blind spot in a
	// user's Player.log while the relay path is fully traced. SECURITY: this sink must NEVER receive
	// a secret - not the symKey, the channel key, or any sealed ciphertext. Only non-sensitive
	// metadata is logged (type, topic, pairing mode, has-relay/has-callback flags, callback host).
	private readonly Action<string>? _log;

	/// <summary><paramref name="relay"/> is optional: without it, relay-enabled pairings
	/// still store their RelayUrl but answers ride the deeplink callback only. <paramref name="log"/>
	/// traces the deeplink lifecycle for diagnostics and MUST never be passed a secret (see field).</summary>
	public LinkDeeplinkEndpoint(WalletLinkV5 dispatcher, IWalletLinkV5Ops ops, ILinkPairingStore pairings, LinkRelayClient? relay = null, Action<string>? log = null)
	{
		_dispatcher = dispatcher;
		_ops = ops;
		_pairings = pairings;
		_relay = relay;
		_log = log;
	}

	/// <summary>Host of a callback URL for logging only - never its path/query/fragment, which can
	/// carry sensitive material. Returns "?" when the URL has no parseable absolute host.</summary>
	private static string CallbackHost(string callback)
	{
		return Uri.TryCreate(callback, UriKind.Absolute, out var uri) ? uri.Host : "?";
	}

	/// <summary>
	/// Handle one OS-delivered URL. Returns true when the URL was a v5 deeplink and was consumed
	/// (even if it was dropped as invalid); false means "not ours", letting the host route it
	/// elsewhere. <paramref name="openUrl"/> is how the wallet opens the dApp's response URL.
	/// </summary>
	public bool TryHandle(string url, Action<string> openUrl)
	{
		if (string.IsNullOrEmpty(url))
		{
			return false;
		}

		if (IsPath(url, "/v5/pair"))
		{
			HandlePairing(url, openUrl);
			return true;
		}

		if (IsPath(url, "/v5/req"))
		{
			HandleRequest(url, openUrl);
			return true;
		}

		// Spec section 19: /v5/wake carries no payload - it only foregrounds the wallet so
		// it (re)connects to the relay and drains the topic mailboxes of its pairings.
		if (IsPath(url, "/v5/wake"))
		{
			_log?.Invoke("wake: ensuring relay connection");
			_relay?.EnsureConnected();
			return true;
		}

		return false;
	}

	private void HandlePairing(string url, Action<string> openUrl)
	{
		LinkPairingParams pairing;
		try
		{
			pairing = LinkPairing.Parse(url);
		}
		catch (FormatException)
		{
			_log?.Invoke("pair drop: malformed pairing material");
			return; // malformed pairing material is dropped, never half-accepted
		}

		var relayUrl = pairing.Relay != null ? LinkRelayClient.NormalizeRelayUrl(pairing.Relay) : null;
		_log?.Invoke($"pair topic={pairing.Topic} mode={pairing.Mode} relay={(relayUrl != null ? "yes" : "no")} callback={(!string.IsNullOrEmpty(pairing.CallbackUrl) ? "yes" : "no")}");

		// Channel-key material decides the mode (spec §18.1):
		//   sym  - the key came in the URI (safe channel); responses go via callback or relay.
		//   ecdh - only the dApp's PUBLIC key came (hijackable custom scheme); the wallet
		//          must answer with its own ephemeral public key, and that hop NEEDS the
		//          relay AND a dApp name (the one-tap push IS the handshake vehicle - without
		//          it the dApp could never derive the key, so such a pairing is useless).
		byte[] channelKey;
		byte[]? walletPublicKey = null;
		if (pairing.Mode == LinkPairingMode.Sym && pairing.SymKey != null)
		{
			// The pairing must offer at least one response path: a deeplink callback or a
			// relay topic (cross-device QR pairings have no usable callback on this device).
			if (string.IsNullOrEmpty(pairing.CallbackUrl) && relayUrl == null)
			{
				_log?.Invoke($"pair drop topic={pairing.Topic} reason=sym-no-return-channel");
				return;
			}
			channelKey = pairing.SymKey;
		}
		else if (pairing.Mode == LinkPairingMode.Ecdh && pairing.DappPublicKey != null)
		{
			if (relayUrl == null || _relay == null || string.IsNullOrEmpty(pairing.DappName))
			{
				_log?.Invoke($"pair drop topic={pairing.Topic} reason=ecdh-needs-relay-and-name");
				return;
			}
			var (publicKey, secretKey) = PhantasmaPhoenix.Cryptography.NaCl.GenerateKeyPair();
			walletPublicKey = publicKey;
			channelKey = PhantasmaPhoenix.Cryptography.NaCl.DeriveSessionKey(pairing.DappPublicKey, secretKey);
		}
		else
		{
			_log?.Invoke($"pair drop topic={pairing.Topic} reason=bad-mode-material");
			return; // malformed mode/material combination
		}

		_ops.ConfirmPairing(pairing, approved =>
		{
			if (!approved)
			{
				_log?.Invoke($"pair consent declined topic={pairing.Topic}");
				return;
			}
			_log?.Invoke($"pair consent approved topic={pairing.Topic}");
			var now = DateTime.UtcNow;
			var record = new LinkPairingRecord
			{
				Topic = pairing.Topic,
				Key = channelKey,
				CallbackUrl = pairing.CallbackUrl ?? "",
				RelayUrl = relayUrl,
				DappName = pairing.DappName ?? "",
				CreatedUtc = now,
				LastSeenUtc = now,
			};
			_pairings.Save(record);
			// A relay-enabled pairing starts listening immediately: the dApp's next request
			// may arrive over the relay rather than a deeplink.
			if (relayUrl != null)
			{
				_relay?.TrackPairing(record);
			}

			// Spec §15 step 3: on approval the wallet returns the encrypted connect result, so
			// the first connection is ONE user gesture (no manual app switch + second consent).
			// The pairing consent text is the consent for this - which is also why the push is
			// gated on a dApp NAME from the pairing meta: a consent dialog that could only show
			// the bare topic must not hand out account data; such dApps keep the classic
			// two-step flow (explicit pha_connect with its own prompt), as does a wallet that
			// is locked or has no account here (EstablishConsentedSession delivers nothing).
			var dappName = pairing.DappName;
			if (string.IsNullOrEmpty(dappName))
			{
				_log?.Invoke($"pair topic={pairing.Topic}: no dapp name, deferring to explicit pha_connect");
				return;
			}
			_dispatcher.EstablishConsentedSession(dappName!, eventJson =>
			{
				// Remember which v5 session this channel established, so an eviction/expiry of the
				// pairing can best-effort notify the dApp with pha_sessionDeleted (spec §7).
				try
				{
					record.SessionId = (string?)Newtonsoft.Json.Linq.JObject.Parse(eventJson)["session"] ?? "";
					_pairings.Save(record);
				}
				catch
				{
					// A malformed event must not break pairing; we just lose the notify-on-evict id.
				}
				// Route the push where the dApp can actually receive it. ecdh always goes via
				// the relay carrying the wallet's public key (the dApp derives the channel key
				// from it); sym prefers the relay when the pairing has one, else the deeplink
				// callback. Exactly one path is used per pairing.
				if (walletPublicKey != null && _relay != null)
				{
					_log?.Invoke($"response via relay handshake topic={record.Topic}");
					_relay.PublishHandshake(record, walletPublicKey, eventJson);
				}
				else if (relayUrl != null && _relay != null)
				{
					_log?.Invoke($"response via relay sealed topic={record.Topic}");
					_relay.PublishSealed(record, eventJson);
				}
				else if (!string.IsNullOrEmpty(pairing.CallbackUrl))
				{
					_log?.Invoke($"response via callback host={CallbackHost(pairing.CallbackUrl!)} topic={record.Topic}");
					var channel = new LinkChannel(channelKey);
					openUrl(BuildResponseUrl(pairing.CallbackUrl!, pairing.Topic, channel.SealEnvelope(eventJson)));
				}
			});
		});
	}

	private void HandleRequest(string url, Action<string> openUrl)
	{
		if (!TryParseRequest(url, out var topic, out var frameJson))
		{
			_log?.Invoke("req drop: unparseable url");
			return;
		}

		var pairing = _pairings.Get(topic);
		if (pairing == null)
		{
			_log?.Invoke($"req drop topic={topic} reason=no-pairing");
			return; // unknown channel; no key to answer with
		}

		var channel = new LinkChannel(pairing.Key);
		if (!channel.TryOpenEnvelope(frameJson, out var envelopeJson))
		{
			_log?.Invoke($"req drop topic={topic} reason=unseal-failed");
			return; // forged or corrupted frame
		}

		_log?.Invoke($"req topic={topic}: dispatching to wallet");
		pairing.LastSeenUtc = DateTime.UtcNow;
		_pairings.Save(pairing);

		_dispatcher.HandleMessage(envelopeJson, responseJson =>
		{
			_log?.Invoke($"req response via callback host={CallbackHost(pairing.CallbackUrl)} topic={topic}");
			var responseFrame = channel.SealEnvelope(responseJson);
			openUrl(BuildResponseUrl(pairing.CallbackUrl, topic, responseFrame));
		});
	}

	/// <summary>Build the wallet->dApp response URL (mirrors the TS SDK's buildResponseUrl).</summary>
	public static string BuildResponseUrl(string callback, string topic, string frame)
	{
		var hashIndex = callback.IndexOf('#');
		var baseUrl = hashIndex < 0 ? callback : callback.Substring(0, hashIndex);
		return $"{baseUrl}#plv=5&t={Uri.EscapeDataString(topic)}&f={LinkEncoding.Base64UrlEncode(Encoding.UTF8.GetBytes(frame))}";
	}

	/// <summary>Parse a dApp->wallet request URL (mirrors the TS SDK's parseRequestUrl).</summary>
	public static bool TryParseRequest(string url, out string topic, out string frameJson)
	{
		topic = "";
		frameJson = "";

		var hashIndex = url.IndexOf('#');
		if (hashIndex < 0 || !url.Substring(0, hashIndex).EndsWith("/v5/req", StringComparison.Ordinal))
		{
			return false;
		}

		string? topicValue = null;
		string? frameValue = null;
		foreach (var pair in url.Substring(hashIndex + 1).Split('&'))
		{
			var eq = pair.IndexOf('=');
			if (eq <= 0) continue;
			var key = pair.Substring(0, eq);
			var value = pair.Substring(eq + 1);
			if (key == "t") topicValue = Uri.UnescapeDataString(value);
			else if (key == "f") frameValue = value;
		}

		if (string.IsNullOrEmpty(topicValue) || string.IsNullOrEmpty(frameValue))
		{
			return false;
		}

		try
		{
			frameJson = Encoding.UTF8.GetString(LinkEncoding.Base64UrlDecode(frameValue));
		}
		catch (FormatException)
		{
			return false;
		}
		topic = topicValue;
		return true;
	}

	private static bool IsPath(string url, string suffix)
	{
		var hashIndex = url.IndexOf('#');
		var withoutFragment = hashIndex < 0 ? url : url.Substring(0, hashIndex);
		return withoutFragment.EndsWith(suffix, StringComparison.Ordinal);
	}
}

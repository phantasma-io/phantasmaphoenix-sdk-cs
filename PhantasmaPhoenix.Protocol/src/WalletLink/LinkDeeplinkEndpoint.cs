using System.Text;

namespace PhantasmaPhoenix.Protocol;

/// <summary>
/// Wallet-side deeplink endpoint for Phantasma Link v5 (spec §19). The host feeds it every URL
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

	public LinkDeeplinkEndpoint(WalletLinkV5 dispatcher, IWalletLinkV5Ops ops, ILinkPairingStore pairings)
	{
		_dispatcher = dispatcher;
		_ops = ops;
		_pairings = pairings;
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
			return; // malformed pairing material is dropped, never half-accepted
		}

		// Phase 2 ships the sym path (QR / universal link). The ecdh custom-scheme fallback
		// needs a wallet-pubkey response hop and lands with the relay work.
		if (pairing.Mode != LinkPairingMode.Sym || pairing.SymKey == null)
		{
			return;
		}
		// A deeplink pairing without a callback is useless: responses would have nowhere to go.
		if (string.IsNullOrEmpty(pairing.CallbackUrl))
		{
			return;
		}

		_ops.ConfirmPairing(pairing, approved =>
		{
			if (!approved)
			{
				return;
			}
			var now = DateTime.UtcNow;
			_pairings.Save(new LinkPairingRecord
			{
				Topic = pairing.Topic,
				Key = pairing.SymKey,
				CallbackUrl = pairing.CallbackUrl!,
				DappName = pairing.DappName ?? "",
				CreatedUtc = now,
				LastSeenUtc = now,
			});

			// Spec §17 step 3: on approval the wallet returns the encrypted connect result, so
			// the first connection is ONE user gesture (no manual app switch + second consent).
			// The pairing consent text is the consent for this - which is also why the push is
			// gated on a dApp NAME from the pairing meta: a consent dialog that could only show
			// the bare topic must not hand out account data; such dApps keep the classic
			// two-step flow (explicit pha_connect with its own prompt), as does a wallet that
			// is locked or has no account here (EstablishConsentedSession delivers nothing).
			var dappName = pairing.DappName;
			if (string.IsNullOrEmpty(dappName))
			{
				return;
			}
			var channel = new LinkChannel(pairing.SymKey);
			_dispatcher.EstablishConsentedSession(dappName!, eventJson =>
				openUrl(BuildResponseUrl(pairing.CallbackUrl!, pairing.Topic, channel.SealEnvelope(eventJson))));
		});
	}

	private void HandleRequest(string url, Action<string> openUrl)
	{
		if (!TryParseRequest(url, out var topic, out var frameJson))
		{
			return;
		}

		var pairing = _pairings.Get(topic);
		if (pairing == null)
		{
			return; // unknown channel; no key to answer with
		}

		var channel = new LinkChannel(pairing.Key);
		if (!channel.TryOpenEnvelope(frameJson, out var envelopeJson))
		{
			return; // forged or corrupted frame
		}

		pairing.LastSeenUtc = DateTime.UtcNow;
		_pairings.Save(pairing);

		_dispatcher.HandleMessage(envelopeJson, responseJson =>
		{
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

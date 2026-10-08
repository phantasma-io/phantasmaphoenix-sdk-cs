using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace PhantasmaPhoenix.Protocol;

// Phantasma Link v5 pairing (spec §15), wallet side. The dApp encodes pairing material in the
// URL FRAGMENT of a universal link / QR / custom-scheme URL; the wallet parses it here. Mirrors
// the TS SDK's pairing.ts byte-for-byte (verified by TS-generated vectors):
//   sym  - fragment carries a 32-byte symmetric session key (safe channels only: QR or a
//          domain-verified universal link),
//   ecdh - fragment carries only the dApp's ephemeral X25519 public key (custom-scheme
//          fallback, where no secret may appear).

public enum LinkPairingMode
{
	Sym,
	Ecdh,
}

/// <summary>Parsed pairing material from a v5 pairing URI.</summary>
public sealed class LinkPairingParams
{
	public int Version { get; set; }
	public string Topic { get; set; } = "";
	public string? Relay { get; set; }
	public LinkPairingMode Mode { get; set; }
	/// <summary>32-byte session key; present when <see cref="Mode"/> is Sym.</summary>
	public byte[]? SymKey { get; set; }
	/// <summary>32-byte dApp X25519 public key; present when <see cref="Mode"/> is Ecdh.</summary>
	public byte[]? DappPublicKey { get; set; }
	/// <summary>Where the wallet opens response deeplinks for this pairing (spec §17).</summary>
	public string? CallbackUrl { get; set; }
	/// <summary>The dApp name from the pairing's <c>meta</c>, or null without a <c>meta</c>.</summary>
	public string? DappName { get; set; }
	/// <summary>The dApp url from the pairing's <c>meta</c>, or null without a <c>meta</c>.</summary>
	public string? DappUrl { get; set; }
	/// <summary>The dApp icon URL from the pairing's <c>meta</c>, or null.</summary>
	public string? DappIcon { get; set; }
}

public static class LinkPairing
{
	// Spec §15: `t` is 32 random bytes in base64url without padding. It is the bearer capability
	// of the pairing, and the relay topics are built from it (§16).
	private static readonly Regex PairingTopic = new Regex("^[A-Za-z0-9_-]{43}$");

	/// <summary>Returns true if <paramref name="topic"/> has the form of a pairing topic: 32 bytes
	/// in base64url without padding, 43 characters (spec §15).</summary>
	public static bool IsPairingTopic(string topic)
	{
		return topic != null && PairingTopic.IsMatch(topic);
	}

	/// <summary>
	/// Parse a pairing URI (universal-link or custom-scheme). Throws <see cref="FormatException"/>
	/// on anything malformed - a wallet must not half-accept pairing material. Refuses what spec
	/// §15 tells a wallet to refuse: a topic that is not 32 bytes in base64url, a <c>relay</c> that
	/// is not a <c>wss://</c> URL, a key that is not 32 bytes, a symmetric key in a
	/// <c>phantasma://</c> URL, a pairing with neither <c>relay</c> nor <c>cb</c>, an ecdh pairing
	/// without a <c>relay</c> or without <c>meta</c>, and a <c>meta</c> without a string
	/// <c>name</c> and <c>url</c>.
	/// </summary>
	public static LinkPairingParams Parse(string uri)
	{
		if (string.IsNullOrEmpty(uri)) throw new FormatException("Pairing URI is empty");

		var hashIndex = uri.IndexOf('#');
		if (hashIndex < 0) throw new FormatException("Pairing URI has no fragment");

		var query = ParseFragment(uri.Substring(hashIndex + 1));

		if (!query.TryGetValue("v", out var versionText) || versionText != WalletLinkV5.ProtocolVersion.ToString())
		{
			throw new FormatException($"Unsupported pairing version: {versionText ?? "<none>"}");
		}

		if (!query.TryGetValue("t", out var topic) || topic.Length == 0)
		{
			throw new FormatException("Pairing URI is missing topic");
		}
		// A longer topic makes relay topics that a relay refuses, and the relay then closes the
		// connection that the wallet shares between its pairings (spec §7).
		if (!IsPairingTopic(topic))
		{
			throw new FormatException("Pairing topic must be 32 bytes in base64url (43 characters)");
		}

		var result = new LinkPairingParams
		{
			Version = WalletLinkV5.ProtocolVersion,
			Topic = topic,
		};

		if (query.TryGetValue("relay", out var relay))
		{
			// Spec §15: the relay is a wss:// URL. A plain ws:// relay would expose the topic, the
			// bearer capability of the pairing, on the network.
			if (!relay.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
			{
				throw new FormatException("Pairing relay must be a wss:// URL");
			}
			result.Relay = relay;
		}

		if (query.TryGetValue("cb", out var callback))
		{
			// Spec §15: a cb that is present is not empty; an empty one is no way back.
			if (callback.Length == 0)
			{
				throw new FormatException("Pairing cb must not be empty");
			}
			result.CallbackUrl = callback;
		}

		var hasMeta = query.TryGetValue("meta", out var metaRaw);
		if (hasMeta)
		{
			ReadMeta(metaRaw!, result);
		}

		var hasSym = query.TryGetValue("sk", out var sk);
		var hasPk = query.TryGetValue("pk", out var pk);
		if (hasSym == hasPk)
		{
			throw new FormatException("Pairing URI must carry exactly one of sk / pk");
		}

		if (hasSym)
		{
			// Spec §15: another app can register the custom scheme and read such a URI, so it must
			// never carry the session key.
			if (uri.StartsWith("phantasma:", StringComparison.OrdinalIgnoreCase))
			{
				throw new FormatException("A custom-scheme pairing URI must not carry a symmetric key");
			}
			result.Mode = LinkPairingMode.Sym;
			result.SymKey = DecodeKey(sk!, "sk");
			// The wallet answers over the relay or by opening the callback; without either it
			// would have no way to answer.
			if (result.Relay == null && result.CallbackUrl == null)
			{
				throw new FormatException("Pairing has neither a relay nor a callback");
			}
		}
		else
		{
			result.Mode = LinkPairingMode.Ecdh;
			result.DappPublicKey = DecodeKey(pk!, "pk");
			// The wallet's public key travels back over the relay, beside the first sealed frame,
			// and only after the user saw the dApp named in `meta`. Without either the dApp would
			// never get the session key.
			if (result.Relay == null)
			{
				throw new FormatException("An ecdh pairing needs a relay");
			}
			if (!hasMeta)
			{
				throw new FormatException("An ecdh pairing needs the dApp meta");
			}
		}

		return result;
	}

	/// <summary>Reads the <c>meta</c> field: the base64url of the UTF-8 JSON of the dApp metadata,
	/// the <c>dapp</c> object of <c>pha_connect</c> with a string <c>name</c> and <c>url</c>
	/// (spec §15). Fields of another JSON type never reach a cast that would throw.</summary>
	private static void ReadMeta(string encoded, LinkPairingParams result)
	{
		JObject meta;
		try
		{
			meta = JObject.Parse(Encoding.UTF8.GetString(LinkEncoding.Base64UrlDecode(encoded)));
		}
		catch (Exception)
		{
			throw new FormatException("Pairing URI has malformed meta");
		}
		if (meta["name"]?.Type != JTokenType.String || meta["url"]?.Type != JTokenType.String)
		{
			throw new FormatException("Pairing meta needs a name and a url");
		}
		result.DappName = (string?)meta["name"];
		result.DappUrl = (string?)meta["url"];
		result.DappIcon = meta["icon"]?.Type == JTokenType.String ? (string?)meta["icon"] : null;
	}

	private static byte[] DecodeKey(string encoded, string field)
	{
		byte[] bytes;
		try
		{
			bytes = LinkEncoding.Base64UrlDecode(encoded);
		}
		catch (Exception)
		{
			throw new FormatException($"Pairing URI has malformed {field}");
		}
		if (bytes.Length != 32)
		{
			throw new FormatException($"Pairing URI {field} must be 32 bytes, got {bytes.Length}");
		}
		return bytes;
	}

	/// <summary>Fragment query parsing (the subset URLSearchParams emits: key=value&amp;...).</summary>
	private static Dictionary<string, string> ParseFragment(string fragment)
	{
		var result = new Dictionary<string, string>();
		foreach (var pair in fragment.Split('&'))
		{
			if (pair.Length == 0) continue;
			var eq = pair.IndexOf('=');
			var key = eq < 0 ? pair : pair.Substring(0, eq);
			var value = eq < 0 ? "" : pair.Substring(eq + 1);
			result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value.Replace("+", "%20"));
		}
		return result;
	}

}

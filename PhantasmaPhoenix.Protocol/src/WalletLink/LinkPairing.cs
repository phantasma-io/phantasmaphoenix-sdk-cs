using System.Text;
using Newtonsoft.Json.Linq;

namespace PhantasmaPhoenix.Protocol;

// Phantasma Link v5 pairing (spec §17), wallet side. The dApp encodes pairing material in the
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
	/// <summary>Where the wallet opens response deeplinks for this pairing (spec §19).</summary>
	public string? CallbackUrl { get; set; }
	public string? DappName { get; set; }
	public string? DappUrl { get; set; }
}

public static class LinkPairing
{
	/// <summary>
	/// Parse a pairing URI (universal-link or custom-scheme). Throws <see cref="FormatException"/>
	/// on anything malformed - a wallet must not half-accept pairing material.
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

		var result = new LinkPairingParams
		{
			Version = WalletLinkV5.ProtocolVersion,
			Topic = topic,
		};

		if (query.TryGetValue("relay", out var relay))
		{
			result.Relay = relay;
		}

		if (query.TryGetValue("cb", out var callback))
		{
			result.CallbackUrl = callback;
		}

		var hasSym = query.TryGetValue("sk", out var sk);
		var hasPk = query.TryGetValue("pk", out var pk);
		if (hasSym == hasPk)
		{
			throw new FormatException("Pairing URI must carry exactly one of sk / pk");
		}

		if (hasSym)
		{
			result.Mode = LinkPairingMode.Sym;
			result.SymKey = DecodeKey(sk!, "sk");
		}
		else
		{
			result.Mode = LinkPairingMode.Ecdh;
			result.DappPublicKey = DecodeKey(pk!, "pk");
		}

		if (query.TryGetValue("meta", out var metaRaw))
		{
			JObject meta;
			try
			{
				meta = JObject.Parse(Encoding.UTF8.GetString(LinkEncoding.Base64UrlDecode(metaRaw)));
			}
			catch (Exception)
			{
				throw new FormatException("Pairing URI has malformed meta");
			}
			result.DappName = (string?)meta["name"];
			result.DappUrl = (string?)meta["url"];
		}

		return result;
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

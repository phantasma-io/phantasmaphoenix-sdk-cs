using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.Cryptography;

namespace PhantasmaPhoenix.Protocol;

/// <summary>
/// An established v5 encrypted channel (spec §8): seals/opens envelope JSON as the wire frame
/// <c>{"nonce": base64, "ct": base64}</c> with NaCl secretbox under the pairing-derived session
/// key. Matches the TS SDK's sealEnvelopeText/openEnvelopeText byte-for-byte. Used by the
/// deeplink and relay transports; plaintext loopback does not need it.
/// </summary>
public sealed class LinkChannel
{
	private readonly byte[] _key;

	public LinkChannel(byte[] sessionKey)
	{
		if (sessionKey == null || sessionKey.Length != NaCl.KeyLength)
		{
			throw new ArgumentException("session key must be 32 bytes", nameof(sessionKey));
		}
		_key = sessionKey;
	}

	/// <summary>Seal envelope JSON into a wire frame with a fresh random nonce.</summary>
	public string SealEnvelope(string envelopeJson)
	{
		var nonce = NaCl.GenerateNonce();
		var sealedBox = NaCl.SecretBoxSeal(Encoding.UTF8.GetBytes(envelopeJson), nonce, _key);
		return new JObject
		{
			["nonce"] = Convert.ToBase64String(nonce),
			["ct"] = Convert.ToBase64String(sealedBox),
		}.ToString(Formatting.None);
	}

	/// <summary>Open a wire frame back into envelope JSON. False on forgery or malformed input.</summary>
	public bool TryOpenEnvelope(string frameJson, out string envelopeJson)
	{
		envelopeJson = "";
		byte[] nonce;
		byte[] sealedBox;
		try
		{
			var frame = JObject.Parse(frameJson);
			nonce = Convert.FromBase64String((string?)frame["nonce"] ?? "");
			sealedBox = Convert.FromBase64String((string?)frame["ct"] ?? "");
		}
		catch (Exception)
		{
			return false;
		}

		if (nonce.Length != NaCl.NonceLength)
		{
			return false;
		}
		if (!NaCl.TrySecretBoxOpen(sealedBox, nonce, _key, out var plaintext))
		{
			return false;
		}
		envelopeJson = Encoding.UTF8.GetString(plaintext);
		return true;
	}
}

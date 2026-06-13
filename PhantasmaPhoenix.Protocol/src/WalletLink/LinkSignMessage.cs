using System.Security.Cryptography;
using System.Text;

namespace PhantasmaPhoenix.Protocol;

/// <summary>
/// The exact byte construction a wallet signs for <c>pha_signMessage</c> (spec §8):
/// <c>DOMAIN_TAG || random(32) || message</c>. The domain tag can never be the prefix of
/// a valid serialized Phantasma transaction, so a message signature can never be replayed
/// as a transaction signature; the 32 CSPRNG bytes replace the legacy protocol's weak
/// 4-byte UnityEngine.Random. This mirrors the TS SDK's buildSignMessagePayload byte for
/// byte - keeping the layout in one place per language is what makes signatures verify
/// across stacks. The signature itself is a RAW 64-byte Ed25519 detached signature over
/// this payload (verifiable with any NaCl implementation given the account public key).
/// </summary>
public static class LinkSignMessage
{
	/// <summary>ASCII bytes of <c>"PHANTASMA_LINK_V5_MSG\n"</c> (same constant as the TS SDK).</summary>
	public static readonly byte[] DomainTag = Encoding.ASCII.GetBytes("PHANTASMA_LINK_V5_MSG\n");

	/// <summary>Length of the CSPRNG random prefix the wallet generates.</summary>
	public const int RandomLength = 32;

	/// <summary>Generate the 32 random payload bytes (cryptographic RNG, never UnityEngine.Random).</summary>
	public static byte[] GenerateRandom()
	{
		var random = new byte[RandomLength];
		using (var rng = RandomNumberGenerator.Create())
		{
			rng.GetBytes(random);
		}
		return random;
	}

	/// <summary>Assemble the exact signed payload; a verifier rebuilds it from the returned
	/// `random` plus the original message.</summary>
	public static byte[] BuildPayload(byte[] message, byte[] random)
	{
		if (random.Length != RandomLength)
		{
			throw new ArgumentException($"signMessage random must be {RandomLength} bytes", nameof(random));
		}
		var payload = new byte[DomainTag.Length + random.Length + message.Length];
		Buffer.BlockCopy(DomainTag, 0, payload, 0, DomainTag.Length);
		Buffer.BlockCopy(random, 0, payload, DomainTag.Length, random.Length);
		Buffer.BlockCopy(message, 0, payload, DomainTag.Length + random.Length, message.Length);
		return payload;
	}
}

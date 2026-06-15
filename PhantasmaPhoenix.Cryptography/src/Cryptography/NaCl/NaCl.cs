using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math.EC.Rfc7748;
using Org.BouncyCastle.Security;

namespace PhantasmaPhoenix.Cryptography;

/// <summary>
/// NaCl channel cryptography for Phantasma Link v5 (spec §8/§18): XSalsa20-Poly1305 secretbox
/// for frame encryption and the X25519 + HSalsa20 key derivation that matches tweetnacl's
/// <c>box.before</c>, so a wallet using this class and a dApp using the TS SDK derive the SAME
/// session key and can open each other's frames byte-for-byte (verified by interop vectors
/// generated from the TS implementation). Built on BouncyCastle primitives; only HSalsa20 is
/// implemented locally because BouncyCastle does not expose it.
/// </summary>
public static class NaCl
{
	public const int KeyLength = 32;
	public const int NonceLength = 24;
	public const int TagLength = 16;
	public const int PublicKeyLength = 32;
	public const int SecretKeyLength = 32;

	private static readonly SecureRandom Random = new SecureRandom();

	/// <summary>Generate a fresh X25519 keypair for the ECDH pairing path.</summary>
	public static (byte[] publicKey, byte[] secretKey) GenerateKeyPair()
	{
		var secretKey = new byte[SecretKeyLength];
		Random.NextBytes(secretKey);
		var publicKey = new byte[PublicKeyLength];
		X25519.GeneratePublicKey(secretKey, 0, publicKey, 0);
		return (publicKey, secretKey);
	}

	/// <summary>Generate a fresh 32-byte symmetric session key (primary QR/universal-link path).</summary>
	public static byte[] GenerateSessionKey()
	{
		var key = new byte[KeyLength];
		Random.NextBytes(key);
		return key;
	}

	/// <summary>Generate a fresh 24-byte secretbox nonce (XSalsa20's size makes random safe).</summary>
	public static byte[] GenerateNonce()
	{
		var nonce = new byte[NonceLength];
		Random.NextBytes(nonce);
		return nonce;
	}

	/// <summary>
	/// Derive the shared session key from an X25519 exchange, exactly like tweetnacl's
	/// <c>box.before</c>: HSalsa20(X25519(mySecret, theirPublic), zero block).
	/// </summary>
	public static byte[] DeriveSessionKey(byte[] theirPublicKey, byte[] mySecretKey)
	{
		if (theirPublicKey == null || theirPublicKey.Length != PublicKeyLength) throw new ArgumentException("public key must be 32 bytes", nameof(theirPublicKey));
		if (mySecretKey == null || mySecretKey.Length != SecretKeyLength) throw new ArgumentException("secret key must be 32 bytes", nameof(mySecretKey));

		var shared = new byte[32];
		X25519.ScalarMult(mySecretKey, 0, theirPublicKey, 0, shared, 0);
		return HSalsa20(shared, new byte[16]);
	}

	/// <summary>
	/// NaCl secretbox: encrypt and authenticate. Returns <c>tag(16) || ciphertext</c>, the same
	/// layout tweetnacl's <c>secretbox</c> produces.
	/// </summary>
	public static byte[] SecretBoxSeal(byte[] plaintext, byte[] nonce, byte[] key)
	{
		if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
		if (nonce == null || nonce.Length != NonceLength) throw new ArgumentException("nonce must be 24 bytes", nameof(nonce));
		if (key == null || key.Length != KeyLength) throw new ArgumentException("key must be 32 bytes", nameof(key));

		// XSalsa20 keystream: the first 32 bytes are the one-time Poly1305 key, the message is
		// XORed against the stream starting at byte 32 (NaCl's crypto_secretbox construction).
		var engine = new XSalsa20Engine();
		engine.Init(true, new ParametersWithIV(new KeyParameter(key), nonce));

		var polyKey = new byte[32];
		engine.ProcessBytes(polyKey, 0, polyKey.Length, polyKey, 0); // XOR over zeros = keystream

		var output = new byte[TagLength + plaintext!.Length];
		engine.ProcessBytes(plaintext, 0, plaintext.Length, output, TagLength);

		Poly1305Tag(polyKey, output, TagLength, plaintext.Length, output, 0);
		return output;
	}

	/// <summary>
	/// NaCl secretbox open: authenticate and decrypt <c>tag(16) || ciphertext</c>. Returns false
	/// on any forgery/corruption without revealing plaintext.
	/// </summary>
	public static bool TrySecretBoxOpen(byte[] sealedBox, byte[] nonce, byte[] key, out byte[] plaintext)
	{
		plaintext = Array.Empty<byte>();
		if (sealedBox == null || sealedBox.Length < TagLength) return false;
		if (nonce == null || nonce.Length != NonceLength) return false;
		if (key == null || key.Length != KeyLength) return false;

		var engine = new XSalsa20Engine();
		engine.Init(false, new ParametersWithIV(new KeyParameter(key), nonce));

		var polyKey = new byte[32];
		engine.ProcessBytes(polyKey, 0, polyKey.Length, polyKey, 0);

		var expectedTag = new byte[TagLength];
		Poly1305Tag(polyKey, sealedBox, TagLength, sealedBox.Length - TagLength, expectedTag, 0);
		if (!ConstantTimeEquals(expectedTag, 0, sealedBox, 0, TagLength)) return false;

		plaintext = new byte[sealedBox.Length - TagLength];
		engine.ProcessBytes(sealedBox, TagLength, plaintext.Length, plaintext, 0);
		return true;
	}

	private static void Poly1305Tag(byte[] polyKey, byte[] data, int dataOff, int dataLen, byte[] tagOut, int tagOff)
	{
		var mac = new Poly1305();
		mac.Init(new KeyParameter(polyKey));
		mac.BlockUpdate(data, dataOff, dataLen);
		mac.DoFinal(tagOut, tagOff);
	}

	private static bool ConstantTimeEquals(byte[] a, int aOff, byte[] b, int bOff, int len)
	{
		int diff = 0;
		for (int i = 0; i < len; i++)
		{
			diff |= a[aOff + i] ^ b[bOff + i];
		}
		return diff == 0;
	}

	/// <summary>
	/// HSalsa20(key, input): the Salsa20 core WITHOUT the final feed-forward, returning words
	/// 0,5,10,15,6,7,8,9. BouncyCastle keeps this internal, so it is implemented here per the
	/// NaCl specification and pinned by the TS interop vectors.
	/// </summary>
	private static byte[] HSalsa20(byte[] key, byte[] input16)
	{
		uint x0 = 0x61707865; // "expa"
		uint x5 = 0x3320646e; // "nd 3"
		uint x10 = 0x79622d32; // "2-by"
		uint x15 = 0x6b206574; // "te k"
		uint x1 = LE32(key, 0);
		uint x2 = LE32(key, 4);
		uint x3 = LE32(key, 8);
		uint x4 = LE32(key, 12);
		uint x11 = LE32(key, 16);
		uint x12 = LE32(key, 20);
		uint x13 = LE32(key, 24);
		uint x14 = LE32(key, 28);
		uint x6 = LE32(input16, 0);
		uint x7 = LE32(input16, 4);
		uint x8 = LE32(input16, 8);
		uint x9 = LE32(input16, 12);

		for (int round = 0; round < 10; round++)
		{
			// column rounds
			x4 ^= Rotl(x0 + x12, 7); x8 ^= Rotl(x4 + x0, 9); x12 ^= Rotl(x8 + x4, 13); x0 ^= Rotl(x12 + x8, 18);
			x9 ^= Rotl(x5 + x1, 7); x13 ^= Rotl(x9 + x5, 9); x1 ^= Rotl(x13 + x9, 13); x5 ^= Rotl(x1 + x13, 18);
			x14 ^= Rotl(x10 + x6, 7); x2 ^= Rotl(x14 + x10, 9); x6 ^= Rotl(x2 + x14, 13); x10 ^= Rotl(x6 + x2, 18);
			x3 ^= Rotl(x15 + x11, 7); x7 ^= Rotl(x3 + x15, 9); x11 ^= Rotl(x7 + x3, 13); x15 ^= Rotl(x11 + x7, 18);
			// row rounds
			x1 ^= Rotl(x0 + x3, 7); x2 ^= Rotl(x1 + x0, 9); x3 ^= Rotl(x2 + x1, 13); x0 ^= Rotl(x3 + x2, 18);
			x6 ^= Rotl(x5 + x4, 7); x7 ^= Rotl(x6 + x5, 9); x4 ^= Rotl(x7 + x6, 13); x5 ^= Rotl(x4 + x7, 18);
			x11 ^= Rotl(x10 + x9, 7); x8 ^= Rotl(x11 + x10, 9); x9 ^= Rotl(x8 + x11, 13); x10 ^= Rotl(x9 + x8, 18);
			x12 ^= Rotl(x15 + x14, 7); x13 ^= Rotl(x12 + x15, 9); x14 ^= Rotl(x13 + x12, 13); x15 ^= Rotl(x14 + x13, 18);
		}

		var output = new byte[32];
		LE32Write(output, 0, x0);
		LE32Write(output, 4, x5);
		LE32Write(output, 8, x10);
		LE32Write(output, 12, x15);
		LE32Write(output, 16, x6);
		LE32Write(output, 20, x7);
		LE32Write(output, 24, x8);
		LE32Write(output, 28, x9);
		return output;
	}

	private static uint Rotl(uint value, int bits) => (value << bits) | (value >> (32 - bits));

	private static uint LE32(byte[] data, int offset) =>
		(uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

	private static void LE32Write(byte[] data, int offset, uint value)
	{
		data[offset] = (byte)value;
		data[offset + 1] = (byte)(value >> 8);
		data[offset + 2] = (byte)(value >> 16);
		data[offset + 3] = (byte)(value >> 24);
	}
}

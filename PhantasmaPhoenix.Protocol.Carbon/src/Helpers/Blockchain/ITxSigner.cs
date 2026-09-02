using PhantasmaPhoenix.Cryptography;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

/// <summary>
/// Anything that can witness a Carbon transaction. It can be keys held in memory, through
/// <see cref="KeyPairSigner"/> over <see cref="PhantasmaKeys"/>. It can also be a hardware wallet or
/// a remote signing service.
/// <para>
/// A signer signs the serialized <see cref="TxMsg"/> bytes it is given and returns the raw 64-byte
/// Ed25519 signature. A hardware or remote signer resolves asynchronously, so the result is a task.
/// The interface never asks for a private key, because that key may live on a device.
/// </para>
/// </summary>
public interface ITxSigner
{
	/// <summary>The 32-byte Ed25519 public key that identifies this witness in the transaction.</summary>
	byte[] PublicKey { get; }

	/// <summary>Signs the serialized message and returns the 64-byte Ed25519 signature.</summary>
	Task<byte[]> SignAsync(byte[] message, CancellationToken cancellationToken = default);
}

/// <summary>
/// An <see cref="ITxSigner"/> over a key pair held in this process. In-memory keys can then sign next
/// to external signers in one witness set. The in-memory-only paths, such as
/// <c>TxMsgSigner.SignWithKeys</c>, take the key pair directly and need no adapter.
/// </summary>
public sealed class KeyPairSigner : ITxSigner
{
	private readonly IKeyPair _keys;

	public KeyPairSigner(IKeyPair keys)
	{
		_keys = keys ?? throw new ArgumentNullException(nameof(keys));
	}

	public byte[] PublicKey => _keys.PublicKey;

	public Task<byte[]> SignAsync(byte[] message, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(Sign(message));
	}

	internal byte[] Sign(byte[] message) => Ed25519.Sign(message, _keys.PrivateKey);
}

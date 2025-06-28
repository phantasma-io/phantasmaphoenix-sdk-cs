using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Cryptography.Extensions;

namespace PhantasmaPhoenix.Cryptography.Legacy.Storage;

public class PrivateArchiveEncryption : IArchiveEncryption
{
	public Address Address { get; private set; }
	private readonly int InitializationVectorSize = 16;
	public byte[] NameInitializationVector { get; private set; }
	public byte[] ContentInitializationVector { get; private set; }

	public PrivateArchiveEncryption(Address publicKey)
	{
		this.Address = publicKey;
		this.NameInitializationVector = AES.GenerateIV(InitializationVectorSize);
		this.ContentInitializationVector = AES.GenerateIV(InitializationVectorSize);
	}

	public PrivateArchiveEncryption()
	{
	}

	public ArchiveEncryptionMode Mode => ArchiveEncryptionMode.Private;

	public string EncryptName(string name, PhantasmaKeys keys)
	{
		if (keys.Address != this.Address)
		{
			throw new ChainException("encryption public address does not match");
		}

		return Base58.Encode(AES.GCMEncrypt(System.Text.Encoding.UTF8.GetBytes(name), keys.PrivateKey, NameInitializationVector));
	}
	public string DecryptName(string name, PhantasmaKeys keys)
	{
		if (keys.Address != this.Address)
		{
			throw new ChainException("encryption public address does not match");
		}

		return System.Text.Encoding.UTF8.GetString(AES.GCMDecrypt(Base58.Decode(name), keys.PrivateKey, NameInitializationVector));
	}
	public byte[] Encrypt(byte[] chunk, PhantasmaKeys keys)
	{
		if (keys.Address != this.Address)
		{
			throw new ChainException("encryption public address does not match");
		}

		return AES.GCMEncrypt(chunk, keys.PrivateKey, ContentInitializationVector);
	}

	public byte[] Decrypt(byte[] chunk, PhantasmaKeys keys)
	{
		if (keys.Address != this.Address)
		{
			throw new ChainException("decryption public address does not match");
		}

		return AES.GCMDecrypt(chunk, keys.PrivateKey, ContentInitializationVector);
	}

	public void SerializeData(BinaryWriter writer)
	{
		writer.WriteAddress(Address);
		writer.Write(NameInitializationVector);
		writer.Write(ContentInitializationVector);
	}

	public void UnserializeData(BinaryReader reader)
	{
		this.Address = reader.ReadAddress();
		this.NameInitializationVector = reader.ReadBytes(InitializationVectorSize);
		this.ContentInitializationVector = reader.ReadBytes(InitializationVectorSize);
	}
}

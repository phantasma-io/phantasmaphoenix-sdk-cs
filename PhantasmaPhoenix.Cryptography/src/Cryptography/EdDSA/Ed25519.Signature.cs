﻿using PhantasmaPhoenix.Core.Extensions;

namespace PhantasmaPhoenix.Cryptography;

public class Ed25519Signature : Signature
{
	public byte[] Bytes { get; private set; }

	public override SignatureKind Kind => SignatureKind.Ed25519;

	internal Ed25519Signature()
	{
		this.Bytes = Array.Empty<byte>();
	}

	public Ed25519Signature(byte[] bytes)
	{
		this.Bytes = bytes;
	}

	public override bool Verify(byte[] message, IEnumerable<Address> addresses)
	{
		foreach (var address in addresses)
		{
			if (!address.IsUser)
			{
				continue;
			}

			var pubKey = address.ToByteArray().Skip(2).ToArray();
			if (Ed25519.Verify(this.Bytes, message, pubKey))
			{
				return true;
			}
		}

		return false;
	}

	public override void SerializeData(BinaryWriter writer)
	{
		writer.WriteByteArray(this.Bytes);
	}

	public override void UnserializeData(BinaryReader reader)
	{
		this.Bytes = reader.ReadByteArray();
	}

	public static Ed25519Signature Generate(IKeyPair keypair, byte[] message)
	{
		var sign = Ed25519.Sign(message, keypair.PrivateKey);
		return new Ed25519Signature(sign);
	}

	public override int GetHashCode()
	{
		return Bytes.GetHashCode() ^ (int)Kind;
	}
	public override bool Equals(object obj)
	{
		if (!(obj is Signature))
		{
			return false;
		}

		if (obj is Ed25519Signature otherEd)
		{
			return this.Bytes.SequenceEqual(otherEd.Bytes);
		}

		return base.Equals(obj);
	}
}

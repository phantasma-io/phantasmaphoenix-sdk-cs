using System.Numerics;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Cryptography.Extensions;

namespace PhantasmaPhoenix.Protocol;

public struct GasEventData
{
	public readonly Address address;
	public readonly BigInteger price;
	public readonly BigInteger amount;

	public GasEventData(Address address, BigInteger price, BigInteger amount)
	{
		this.address = address;
		this.price = price;
		this.amount = amount;
	}

	public byte[] Serialize()
	{
			using var buffer = new MemoryStream();
			using var writer = new BinaryWriter(buffer);
			writer.WriteAddress(address);
			writer.WriteBigInteger(price);
			writer.WriteBigInteger(amount);
			return buffer.ToArray();
	}
}

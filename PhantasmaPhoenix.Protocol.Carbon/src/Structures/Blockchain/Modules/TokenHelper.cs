namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;

public static class SystemAddress
{
	private static Bytes32 Create(byte lastByte)
	{
		var bytes = new byte[32];
		bytes[31] = lastByte;
		return new Bytes32 { bytes = bytes };
	}

	public static Bytes32 Null = Create(0); // All elements default to 0
	public static Bytes32 GasPool = Create(1);
	public static Bytes32 DataPool = Create(2);
}

public static class TokenHelper
{
	public static Bytes32 GetNftAddress(UInt64 tokenId, UInt64 instanceId)
	{
		var address = new Bytes32();
		address.bytes[15] = 1;
		Array.Copy(BitConverter.GetBytes(tokenId), 0, address.bytes, 16, 8);
		Array.Copy(BitConverter.GetBytes(instanceId), 0, address.bytes, 24, 8);
		return address;
	}

	/// <summary>
	/// Returns true if a 32-byte address is an NFT-derived address. Every minted instance owns such an
	/// address, and assets infused into that NFT are sent to it.
	/// <para>
	/// The test is syntactic and is the one the chain applies: fifteen zero bytes, a 0x01 marker, then
	/// a nonzero token id and a nonzero instance id. <c>FeePlanner.Plan</c> uses it to price the
	/// recipient's owner lookup.
	/// </para>
	/// </summary>
	public static bool IsNftAddress(Bytes32 address) => IsNftAddress(address.bytes);

	public static bool IsNftAddress(byte[] address)
	{
		if (address == null || address.Length != 32 || address[15] != 1)
			return false;
		for (var i = 0; i < 15; i++)
		{
			if (address[i] != 0)
				return false;
		}
		return BitConverter.ToUInt64(address, 16) != 0 && BitConverter.ToUInt64(address, 24) != 0;
	}

	public static void UnpackNftInstanceId(UInt64 instanceId, out uint seriesId, out uint mintNumber)
	{
		seriesId = (uint)(instanceId & 0xFFFFFFFF);
		mintNumber = (uint)((instanceId >> 32) & 0xFFFFFFFF);
	}
}

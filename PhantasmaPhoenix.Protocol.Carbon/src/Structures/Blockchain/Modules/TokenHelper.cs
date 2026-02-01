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

	public static void UnpackNftInstanceId(UInt64 instanceId, out uint seriesId, out uint mintNumber)
	{
		seriesId = (uint)(instanceId & 0xFFFFFFFF);
		mintNumber = (uint)((instanceId >> 32) & 0xFFFFFFFF);
	}
}

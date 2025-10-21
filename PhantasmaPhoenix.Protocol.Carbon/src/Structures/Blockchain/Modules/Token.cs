namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;

public static class SystemAddress
{
	public static Bytes32 Null = new Bytes32 { bytes = new byte[32] }; // All elements default to 0
}

public static class Token
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

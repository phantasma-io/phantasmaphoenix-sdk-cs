using PhantasmaPhoenix.Cryptography;

namespace PhantasmaPhoenix.Protocol;

public struct TransactionSettleEventData
{
	public readonly Hash Hash;
	public readonly string Platform;
	public readonly string Chain;

	public TransactionSettleEventData(Hash hash, string platform, string chain)
	{
		Hash = hash;
		Platform = platform;
		Chain = chain;
	}
}

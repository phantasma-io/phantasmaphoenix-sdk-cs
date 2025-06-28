namespace PhantasmaPhoenix.InteropChains.Legacy.Ethereum.RLP;
/// <summary>
///     Wrapper class for decoded elements from an RLP encoded byte array.
/// </summary>
public interface IRLPElement
{
	byte[] RLPData { get; }
}

namespace PhantasmaPhoenix.InteropChains.Legacy.Ethereum.RLP;

public class RLPCollection : List<IRLPElement>, IRLPElement
{
	public byte[] RLPData { get; set; }
}

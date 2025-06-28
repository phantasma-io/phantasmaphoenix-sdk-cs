using System.Numerics;
using PhantasmaPhoenix.Core.Extensions;

namespace PhantasmaPhoenix.Protocol;

public struct TokenEventData
{
    public readonly string Symbol;
    public readonly BigInteger Value;
    public readonly string ChainName;

    public TokenEventData(string symbol, BigInteger value, string chainName)
    {
        this.Symbol = symbol;
        this.Value = value;
        this.ChainName = chainName;
    }

	public byte[] Serialize()
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.WriteVarString(Symbol);
        writer.WriteBigInteger(Value);
        writer.WriteVarString(ChainName);
        return buffer.ToArray();
	}
}

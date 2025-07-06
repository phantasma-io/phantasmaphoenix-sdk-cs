using PhantasmaPhoenix.Cryptography;

namespace PhantasmaPhoenix.RPC.Models;

public class TransactionResult
{
	public string Hash { get; set; }
	public string ChainAddress { get; set; }
	public UInt64 Timestamp { get; set; }
	public UInt64 BlockHeight { get; set; }
	public string BlockHash { get; set; }
	public string Script { get; set; }
	public string Payload { get; set; }
	public string? DebugComment { get; set; }
	public EventResult[] Events { get; set; }
	public string Result { get; set; }
	public string Fee { get; set; }
	public string State { get; set; }
	public TransactionSignatureResult[]? Signatures { get; set; }
	public string Sender { get; set; } = Address.Null.Text; // Initialized as in original Phantasma code.
	public string GasPayer { get; set; }
	public string GasTarget { get; set; } = "NULL";
	public string GasPrice { get; set; } = "";
	public string GasLimit { get; set; }
	public UInt64 Expiration { get; set; }

	// TODO check later if still needed
	public TransactionResult()
	{
	}
}

using Newtonsoft.Json;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;

namespace PhantasmaPhoenix.RPC.Models;

public class TransactionResult
{
	public string Hash { get; set; } = "";
	public string? ChainAddress { get; set; }
	public UInt64 Timestamp { get; set; }
	public UInt64 BlockHeight { get; set; }
	public string? BlockHash { get; set; }
	public string? Script { get; set; }
	public byte CarbonTxType { get; set; }

	// The transaction message bytes. Hex on the wire exactly as before; the type carries the bytes so
	// that a node answering a multi-megabyte transaction never builds the hex string in memory. Reading
	// this property as a string still works and yields the same hex.
	[JsonConverter(typeof(HexBytesJsonConverter))]
	public HexBytes? CarbonTxData { get; set; }
	public string? Payload { get; set; }
	public string? DebugComment { get; set; }
	public EventResult[]? Events { get; set; }
	public EventExResult[]? ExtendedEvents { get; set; }
	public string? Result { get; set; }
	public string? Fee { get; set; }
	public ExecutionState State { get; set; }
	public TransactionSignatureResult[]? Signatures { get; set; }
	public string Sender { get; set; } = Address.Null.Text; // Initialized as in original Phantasma code.
	public string? GasPayer { get; set; }
	public string GasTarget { get; set; } = "NULL";
	public string GasPrice { get; set; } = "";
	public string? GasLimit { get; set; }
	public UInt64 Expiration { get; set; }

	public TransactionResult() { }
}

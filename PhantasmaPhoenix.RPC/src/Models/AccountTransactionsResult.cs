using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class AccountTransactionsResult
{
	public string Address { get; set; }

	[ApiDescription("List of transactions")]
	public TransactionResult[] Txs { get; set; }

	public AccountTransactionsResult() { }
}

using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Types;

public class CursorPaginatedResult<T>
{
	public T? Result { get; set; }

	[ApiDescription("Cursor to request next page of results")]
	public string? Cursor { get; set; }

	public CursorPaginatedResult(T? result, string? cursor)
	{
		Result = result;
		Cursor = cursor;
	}
}

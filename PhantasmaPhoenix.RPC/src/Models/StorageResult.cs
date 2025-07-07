using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class StorageResult
{
	[ApiDescription("Amount of available storage bytes")]
	public uint Available { get; set; }

	[ApiDescription("Amount of used storage bytes")]
	public uint Used { get; set; }

	[ApiDescription("Avatar data")]
	public string Avatar { get; set; }

	[ApiDescription("List of stored files")]
	public ArchiveResult[] Archives { get; set; }

	public StorageResult() { }
}

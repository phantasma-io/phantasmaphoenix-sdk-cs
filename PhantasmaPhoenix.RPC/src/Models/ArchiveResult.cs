using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class ArchiveResult
{
	[ApiDescription("File name")]
	public string Name { get; set; }

	[ApiDescription("Archive hash")]
	public string Hash { get; set; }

	[ApiDescription("Time of creation")]
	public uint Time { get; set; }

	[ApiDescription("Size of archive in bytes")]
	public uint Size { get; set; }

	[ApiDescription("Encryption address")]
	public string Encryption { get; set; }

	[ApiDescription("Number of blocks")]
	public int BlockCount { get; set; }

	[ApiDescription("Missing block indices")]
	public int[] MissingBlocks { get; set; }

	[ApiDescription("List of addresses who own the file")]
	public string[] Owners { get; set; }

	public ArchiveResult() { }
}

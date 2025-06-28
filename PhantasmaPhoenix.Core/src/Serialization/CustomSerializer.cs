namespace PhantasmaPhoenix.Core;

public struct CustomSerializer
{
	public readonly CustomReader Read;
	public readonly CustomWriter Write;

	public CustomSerializer(CustomReader reader, CustomWriter writer)
	{
		Read = reader;
		Write = writer;
	}
}

namespace PhantasmaPhoenix.Core;

/// <summary>
/// A JSON writer that can emit a hex string value without the caller building that string first.
/// </summary>
/// <remarks>
/// Implemented by writers that can stream a string value in pieces. <see cref="HexBytes"/> asks for
/// this interface and falls back to an ordinary string value when the writer in hand does not offer
/// it, so behaviour is identical either way - only the peak allocation differs.
/// </remarks>
public interface IHexValueWriter
{
	/// <summary>
	/// Writes <paramref name="count"/> bytes starting at <paramref name="offset"/> as one uppercase
	/// hex JSON string value, including its quotes and any separator the writer's state requires.
	/// </summary>
	void WriteHexValue(byte[] array, int offset, int count);
}

using System.Collections.Generic;
using Newtonsoft.Json;

namespace PhantasmaPhoenix.Protocol;

/// <summary>
/// A value decoded from VM storage: a scalar, an array, or a struct.
/// </summary>
/// <remarks>
/// VM values are dynamically typed, so this is a union rather than a fixed model. At most one of
/// <see cref="Text"/>, <see cref="Items"/> and <see cref="Fields"/> is set: a scalar whose content the
/// producer could not read carries none of them and travels as JSON null. That state is real chain
/// data, not a defensive case - a shared-ROM NFT property can have no string content at all, and it is
/// still answered so that its key does not disappear - so a consumer reading <see cref="Text"/> must
/// handle null.
///
/// On the wire it is the plain JSON value - a string, an array or an object - never a JSON document
/// packed into a string, which is what these values used to be.
///
/// Scalars are always <see cref="Text"/>: numbers arrive as decimal strings because chain values are
/// big integers and JSON numbers lose precision above 2^53 in JavaScript consumers; bytes arrive as
/// hex. Reading a scalar therefore stays as simple as it was - <c>string s = property.Value;</c> still
/// compiles through the implicit conversion below.
/// </remarks>
[JsonConverter(typeof(VmValueJsonConverter))]
public sealed class VmValue
{
	public string? Text { get; }

	public VmValue[]? Items { get; }

	public Dictionary<string, VmValue>? Fields { get; }

	private VmValue(string? text, VmValue[]? items, Dictionary<string, VmValue>? fields)
	{
		Text = text;
		Items = items;
		Fields = fields;
	}

	/// <summary>
	/// A scalar. Null text is accepted on purpose - it means the value has no readable content - so a
	/// producer does not have to suppress a warning to express it, and the state stays visible in
	/// <see cref="Text"/>, which consumers already have to null-check.
	/// </summary>
	public static VmValue FromText(string? text) => new VmValue(text, null, null);

	public static VmValue FromItems(VmValue[] items) => new VmValue(null, items, null);

	public static VmValue FromFields(Dictionary<string, VmValue> fields) => new VmValue(null, null, fields);

	/// <summary>True when this is a scalar, i.e. <see cref="Text"/> carries the whole value.</summary>
	public bool IsText => Text != null;

	public override string ToString() => Text ?? string.Empty;

	/// <summary>Scalar shortcut: yields <see cref="Text"/>, or null for an array or a struct.</summary>
	public static implicit operator string?(VmValue? value) => value?.Text;
}

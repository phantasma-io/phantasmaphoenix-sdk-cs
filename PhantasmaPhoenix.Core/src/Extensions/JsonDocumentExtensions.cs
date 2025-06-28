using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PhantasmaPhoenix.Core.Extensions;

public static class JsonDocumentExtensions
{
	public static string ToJsonString(this JToken token)
	{
		return token.ToString(Formatting.Indented);
	}
}

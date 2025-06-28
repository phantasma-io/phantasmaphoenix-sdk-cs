using Newtonsoft.Json.Linq;

namespace PhantasmaPhoenix.Protocol;

public interface IAPIResult
{
}

public struct ErrorResult : IAPIResult
{
	public string error;
}

public struct SingleResult : IAPIResult
{
	public object value;
}

public struct ArrayResult : IAPIResult
{
	public object[] values;
}

public static class APIUtils
{
	public static JToken FromAPIResult(IAPIResult input)
	{
		return JToken.FromObject(input);
	}
}

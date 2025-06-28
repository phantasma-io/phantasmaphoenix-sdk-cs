namespace PhantasmaPhoenix.VM;

public static class Validation
{
	public static bool IsValidMethod(string methodName, VMType returnType)
	{
		if (string.IsNullOrEmpty(methodName) || methodName.Length < 3)
		{
			return false;
		}

		if (methodName.StartsWith("is") && char.IsUpper(methodName[2]))
		{
			return returnType == VMType.Bool;
		}

		// trigger
		if (methodName.StartsWith("on") && char.IsUpper(methodName[2]))
		{
			return returnType == VMType.None;
		}

		// property
		if (methodName.StartsWith("get") && char.IsUpper(methodName[3]))
		{
			return returnType != VMType.None;
		}

		return true;
	}
}

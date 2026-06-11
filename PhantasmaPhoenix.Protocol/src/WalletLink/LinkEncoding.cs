namespace PhantasmaPhoenix.Protocol;

/// <summary>base64url (no padding) as the TS SDK emits in v5 URL fragments.</summary>
internal static class LinkEncoding
{
	public static string Base64UrlEncode(byte[] bytes)
	{
		return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}

	public static byte[] Base64UrlDecode(string text)
	{
		var s = text.Replace('-', '+').Replace('_', '/');
		switch (s.Length % 4)
		{
			case 2: s += "=="; break;
			case 3: s += "="; break;
			case 1: throw new FormatException("Invalid base64url length");
		}
		return Convert.FromBase64String(s);
	}
}

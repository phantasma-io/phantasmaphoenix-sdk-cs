using System.Security.Cryptography;
using System.Text;

namespace PhantasmaPhoenix.Protocol.Tests;

/// <summary>Pairing topics for the tests. A wallet accepts only the form of spec §15: 32 bytes in
/// base64url without padding, 43 characters. A readable label keeps each test's topics apart.</summary>
internal static class LinkTestTopics
{
	/// <summary>Returns the pairing topic of <paramref name="label"/>: the SHA-256 of the label in
	/// base64url without padding.</summary>
	public static string Of(string label)
	{
		using var sha = SHA256.Create();
		var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(label));
		return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}
}

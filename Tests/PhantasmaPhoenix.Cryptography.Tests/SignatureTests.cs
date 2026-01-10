using System.Text;
using PhantasmaPhoenix.Cryptography;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Cryptography.Tests;

public class SignatureTests
{
	private const string TestWif = "KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d";

	[Fact]
	public void Wif_roundtrip_and_signature_verify()
	{
		var keys = PhantasmaKeys.FromWIF(TestWif);
		keys.ToWIF().ShouldBe(TestWif);

		var message = Encoding.UTF8.GetBytes("hello world");
		var signature = keys.Sign(message);

		signature.Verify(message, keys.Address).ShouldBeTrue();

		var badMessage = Encoding.UTF8.GetBytes("hello worlds");
		signature.Verify(badMessage, keys.Address).ShouldBeFalse();
	}
}

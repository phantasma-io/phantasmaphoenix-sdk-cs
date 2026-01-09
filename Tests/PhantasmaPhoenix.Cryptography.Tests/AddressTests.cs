using PhantasmaPhoenix.Cryptography;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Cryptography.Tests;

public class AddressTests
{
	private const string TestWif = "L5UEVHBjujaR1721aZM5Zm5ayjDyamMZS9W35RE9Y9giRkdf3dVx";
	private const string ExpectedAddressText = "P2KFEyFevpQfSaW8G4VjSmhWUZXR4QrG9YQR1HbMpTUCpCL";

	[Fact]
	public void FromWif_produces_expected_address()
	{
		var keys = PhantasmaKeys.FromWIF(TestWif);
		keys.Address.Text.ShouldBe(ExpectedAddressText);
	}

	[Fact]
	public void FromText_roundtrips_text()
	{
		var address = Address.Parse(ExpectedAddressText);
		address.Text.ShouldBe(ExpectedAddressText);
	}

	[Fact]
	public void GetPublicKey_returns_public_key_bytes()
	{
		var keys = PhantasmaKeys.FromWIF(TestWif);

		var publicKey = keys.Address.GetPublicKey();

		publicKey.Length.ShouldBe(32);
		publicKey.ShouldBe(keys.PublicKey);
	}

	[Fact]
	public void Null_address_public_key_is_zeroed()
	{
		var publicKey = Address.Null.GetPublicKey();

		publicKey.Length.ShouldBe(32);
		publicKey.All(b => b == 0).ShouldBeTrue();
	}
}

using PhantasmaPhoenix.Cryptography;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

public class GoldenFixtureInventoryTests
{
	private const string Ed25519FixtureSha256 = "dd747f5c49b49a67f1c63d02351be669558bf9da65571ed7311bcd8cf8d2bd01";

	private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");

	private static readonly string[] SharedFixtureFiles =
	{
		"carbon_vectors.tsv",
		"carbon_tx_builder_vectors.tsv",
		"ed25519_vectors.tsv",
		"gen2_csharp_vm_bigint_binary.tsv",
		"gen2_csharp_vm_bigint_decimal.tsv",
		"gen2_csharp_vm_bigint_narrow_int.tsv",
		"gen2_csharp_vm_bigint_ops.tsv",
		"gen2_csharp_vm_bigint_unary_ops.tsv",
		"gen2_csharp_vm_scriptcontext_ops.tsv",
		"gen2_csharp_vm_scriptcontext_unary.tsv",
		"gen2_csharp_vmobject_arraytype.tsv",
		"gen2_csharp_vmobject_asbool.tsv",
		"gen2_csharp_vmobject_asbytes.tsv",
		"gen2_csharp_vmobject_asnumber.tsv",
		"gen2_csharp_vmobject_asstring.tsv",
		"gen2_csharp_vmobject_cast_struct.tsv",
		"gen2_csharp_vmobject_serde.tsv",
		"phantasma_bigint_vectors.tsv",
		"validator_int256_fixtures.json",
		"vm_script_builder_vectors.tsv",
	};

	public static IEnumerable<object[]> Ed25519Cases => ReadEd25519Rows().Select(row => new object[] { row });

	[Fact]
	public void Shared_golden_fixture_set_is_present_and_readable()
	{
		// This is an inventory gate: every shared fixture file must be shipped
		// with the test assembly and contain at least one non-comment data row.
		foreach (var file in SharedFixtureFiles)
		{
			var path = Path.Combine(FixtureDir, file);
			File.Exists(path).ShouldBeTrue($"missing shared fixture {file}");
			File.ReadLines(path).Any(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#')).ShouldBeTrue($"empty shared fixture {file}");
		}
	}

	[Theory]
	[MemberData(nameof(Ed25519Cases))]
	public void Ed25519_matches_shared_golden_vectors(Ed25519Vector vector)
	{
		// The shared Ed25519 vectors lock key derivation, signing, successful
		// verification, and a negative verification check through public SDK APIs.
		var seed = Convert.FromHexString(vector.SeedHex);
		var message = Convert.FromHexString(vector.MessageHex);
		var publicKey = Convert.FromHexString(vector.PublicKeyHex);
		var expectedSignature = Convert.FromHexString(vector.SignatureHex);

		Ed25519.PublicKeyFromSeed(seed).ShouldBe(publicKey);
		var signature = Ed25519.Sign(message, seed);
		signature.ShouldBe(expectedSignature);
		Ed25519.Verify(signature, message, publicKey).ShouldBeTrue();

		var wrongMessage = message.Length > 0 ? message.ToArray() : new byte[] { 0 };
		wrongMessage[0] ^= 0xff;
		Ed25519.Verify(signature, wrongMessage, publicKey).ShouldBeFalse();
	}

	private static IReadOnlyList<Ed25519Vector> ReadEd25519Rows()
	{
		var path = Path.Combine(FixtureDir, "ed25519_vectors.tsv");
		Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))
			.ToLowerInvariant()
			.ShouldBe(Ed25519FixtureSha256);

		return File.ReadLines(path)
			.Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#') && !line.StartsWith("case_id\t"))
			.Select(line =>
			{
				var parts = line.Split('\t');
				parts.Length.ShouldBe(7);
				return new Ed25519Vector(parts[0], parts[2], parts[3], parts[4], parts[5]);
			})
			.ToArray();
	}

	public sealed record Ed25519Vector(string CaseId, string SeedHex, string PublicKeyHex, string MessageHex, string SignatureHex);
}

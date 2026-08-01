using System;
using System.Collections.Generic;
using PhantasmaPhoenix.Cryptography;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Cryptography.Tests;

// Address compares by content, so it must hash by content too. It previously returned the identity hash
// of its backing array, which meant two addresses that compare equal produced different hash codes as
// soon as they came from different arrays - silently breaking any Dictionary, HashSet, Distinct or
// GroupBy keyed by Address.
public class AddressKeyContractTests
{
	private const string SampleAddressText = "P2KFEyFevpQfSaW8G4VjSmhWUZXR4QrG9YQR1HbMpTUCpCL";

	[Fact]
	public void Address_declares_typed_equality()
	{
		typeof(IEquatable<Address>).IsAssignableFrom(typeof(Address)).ShouldBeTrue();
	}

	[Fact]
	public void Equal_addresses_built_separately_share_a_hash_code()
	{
		var left = Address.Parse(SampleAddressText);
		var right = Address.FromBytes(Address.Parse(SampleAddressText).ToByteArray());

		left.Equals(right).ShouldBeTrue();
		left.GetHashCode().ShouldBe(right.GetHashCode());
	}

	[Fact]
	public void Dictionary_lookup_finds_an_equal_address_from_another_instance()
	{
		var map = new Dictionary<Address, int> { [Address.Parse(SampleAddressText)] = 42 };

		map[Address.FromBytes(Address.Parse(SampleAddressText).ToByteArray())].ShouldBe(42);
		map.Count.ShouldBe(1);
	}

	[Fact]
	public void Distinct_addresses_do_not_collapse_into_one_hash_code()
	{
		var hashes = new HashSet<int>();
		for (var i = 0; i < 500; i++)
		{
			var bytes = new byte[Address.LengthInBytes];
			bytes[0] = (byte)AddressKind.User;
			// Vary the tail so that all 500 addresses share their leading bytes.
			BitConverter.GetBytes(i).CopyTo(bytes, bytes.Length - sizeof(int));
			hashes.Add(Address.FromBytes(bytes).GetHashCode());
		}

		hashes.Count.ShouldBeGreaterThan(450);
	}

	[Fact]
	public void Null_and_default_addresses_agree_on_equality_and_hash_code()
	{
		// Address.Equals treats a default-initialized address (no backing array) as equal to
		// Address.Null, so both have to land on the same hash code.
		var uninitialized = default(Address);

		uninitialized.Equals(Address.Null).ShouldBeTrue();
		Address.Null.Equals(uninitialized).ShouldBeTrue();
		uninitialized.GetHashCode().ShouldBe(Address.Null.GetHashCode());
	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

// Bytes32/Bytes64 are used as dictionary and set keys (transaction ids, node ids, balance owners), so
// their equality contract is load-bearing rather than cosmetic. These tests pin the two properties a
// key type must have: a hash derived from the whole value, and typed equality that the generic
// collections can use without boxing.
public class ByteBlobKeyContractTests
{
	private static Bytes32 Bytes32With(int distinguishingValue, byte fill = 0)
	{
		// The distinguishing bytes sit at the END so that every value in a batch shares its first bytes.
		// A hash over a prefix only would map the whole batch to one bucket.
		var bytes = new byte[32];
		for (var i = 0; i < bytes.Length; i++)
			bytes[i] = fill;
		BitConverter.GetBytes(distinguishingValue).CopyTo(bytes, bytes.Length - sizeof(int));
		return new Bytes32(bytes);
	}

	private static Bytes64 Bytes64With(int distinguishingValue)
	{
		var bytes = new byte[64];
		BitConverter.GetBytes(distinguishingValue).CopyTo(bytes, bytes.Length - sizeof(int));
		return new Bytes64(bytes);
	}

	[Fact]
	public void Bytes32_declares_typed_equality()
	{
		// Without IEquatable the generic collections fall back to the object comparer, which boxes both
		// operands on every comparison.
		typeof(IEquatable<Bytes32>).IsAssignableFrom(typeof(Bytes32)).ShouldBeTrue();
		typeof(IEquatable<Bytes64>).IsAssignableFrom(typeof(Bytes64)).ShouldBeTrue();
	}

	[Fact]
	public void Bytes32_values_sharing_a_prefix_do_not_share_a_hash_code()
	{
		var hashes = Enumerable.Range(0, 1000)
			.Select(i => Bytes32With(i).GetHashCode())
			.ToHashSet();

		// A prefix hash produces exactly one bucket here; a content hash produces essentially 1000.
		hashes.Count.ShouldBeGreaterThan(900);
	}

	[Fact]
	public void Bytes64_values_sharing_a_prefix_do_not_share_a_hash_code()
	{
		var hashes = Enumerable.Range(0, 1000)
			.Select(i => Bytes64With(i).GetHashCode())
			.ToHashSet();

		hashes.Count.ShouldBeGreaterThan(900);
	}

	[Fact]
	public void Bytes32_equal_values_from_different_arrays_share_a_hash_code()
	{
		var left = Bytes32With(7);
		var right = Bytes32With(7);

		ReferenceEquals(left.bytes, right.bytes).ShouldBeFalse();
		left.Equals(right).ShouldBeTrue();
		left.GetHashCode().ShouldBe(right.GetHashCode());
	}

	[Fact]
	public void Bytes32_dictionary_lookups_find_prefix_sharing_keys()
	{
		// The end-to-end property the hash exists for: a value stored under one instance is found again
		// through an equal instance, and a batch of prefix-sharing keys stays a real hash lookup.
		var map = new Dictionary<Bytes32, int>();
		for (var i = 0; i < 1000; i++)
			map[Bytes32With(i)] = i;

		map.Count.ShouldBe(1000);
		for (var i = 0; i < 1000; i++)
			map[Bytes32With(i)].ShouldBe(i);
	}

	[Fact]
	public void Bytes32_default_value_has_no_backing_array_and_still_compares()
	{
		// A default-initialized Bytes32 carries a null array; hashing or comparing it must not throw.
		var uninitialized = default(Bytes32);

		uninitialized.GetHashCode();
		uninitialized.Equals(default(Bytes32)).ShouldBeTrue();
		uninitialized.Equals(Bytes32.Empty).ShouldBeFalse();
		Bytes32.Empty.Equals(uninitialized).ShouldBeFalse();
	}
}

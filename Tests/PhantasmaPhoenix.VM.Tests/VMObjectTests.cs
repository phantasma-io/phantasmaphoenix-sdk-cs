using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.VM;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.VM.Tests;

public class VMObjectTests
{
	[Fact]
	public void Default_vm_object_is_empty()
	{
		var vm = new VMObject();

		vm.Type.ShouldBe(VMType.None);
		vm.IsEmpty.ShouldBeTrue();
	}

	[Fact]
	public void String_vm_object_roundtrips()
	{
		var vm = VMObject.FromObject("MyString");

		vm.Type.ShouldBe(VMType.String);
		vm.AsString().ShouldBe("MyString");
	}

	[Fact]
	public void Number_vm_object_roundtrips()
	{
		var vm = VMObject.FromObject(5);

		vm.Type.ShouldBe(VMType.Number);
		vm.AsString().ShouldBe("5");
		vm.AsNumber().ShouldBe(new System.Numerics.BigInteger(5));
	}

	[Fact]
	public void Decode_bool_from_bytes()
	{
		var bytes = "0601".FromHex()!;
		var vm = VMObject.FromBytes(bytes);

		vm.Type.ShouldBe(VMType.Bool);
		vm.AsBool().ShouldBeTrue();
	}
}

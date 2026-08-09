using Newtonsoft.Json;
using PhantasmaPhoenix.Protocol;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Tests;

// VM values are dynamically typed, so they travel as the plain JSON value - a string, an array or an
// object. These tests pin that shape in both directions, because it is what consumers read.
public class VmValueTests
{
	[Fact]
	public void Scalar_is_a_plain_json_string()
	{
		var value = JsonConvert.DeserializeObject<VmValue>("\"Crown #1\"")!;

		value.IsText.ShouldBeTrue();
		value.Text.ShouldBe("Crown #1");
		JsonConvert.SerializeObject(value).ShouldBe("\"Crown #1\"");
	}

	[Fact]
	public void Struct_keeps_its_fields()
	{
		var value = JsonConvert.DeserializeObject<VmValue>("""{"name":"Crown","level":"3"}""")!;

		value.Fields.ShouldNotBeNull();
		value.Fields!["name"].Text.ShouldBe("Crown");
		value.Fields["level"].Text.ShouldBe("3");
		JsonConvert.SerializeObject(value).ShouldBe("""{"name":"Crown","level":"3"}""");
	}

	[Fact]
	public void Array_keeps_its_items_including_nested_structs()
	{
		var value = JsonConvert.DeserializeObject<VmValue>("""["AA",{"id":"7"}]""")!;

		value.Items!.Length.ShouldBe(2);
		value.Items[0].Text.ShouldBe("AA");
		value.Items[1].Fields!["id"].Text.ShouldBe("7");
		JsonConvert.SerializeObject(value).ShouldBe("""["AA",{"id":"7"}]""");
	}

	// A number or a boolean is not what this RPC emits - chain numbers are decimal strings - but a
	// hand-written or older payload can still carry one, and it must not throw.
	[Fact]
	public void Json_numbers_and_booleans_are_accepted_as_text()
	{
		JsonConvert.DeserializeObject<VmValue>("42")!.Text.ShouldBe("42");
		JsonConvert.DeserializeObject<VmValue>("true")!.Text.ShouldBe("True");
	}

	[Fact]
	public void Scalar_converts_implicitly_to_string()
	{
		string? text = VmValue.FromText("Crown");

		text.ShouldBe("Crown");
	}

	// A scalar with no readable content is real chain data - a shared-ROM NFT property whose VM entry
	// carries no string - and it has to travel as JSON null. The text "null" would be indistinguishable
	// from a property whose value really is that word.
	[Fact]
	public void Scalar_without_content_is_json_null()
	{
		var value = VmValue.FromText(null);

		value.IsText.ShouldBeFalse();
		value.Text.ShouldBeNull();
		value.ToString().ShouldBe(string.Empty);
		JsonConvert.SerializeObject(value).ShouldBe("null");
	}
}

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.RPC.Types;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.RPC.Tests;

public class RpcIdTests
{
	[Fact]
	public void RpcRequest_Preserves_Integer_String_Null_And_Omitted_Ids()
	{
		var numeric = JsonConvert.DeserializeObject<RpcRequest>(
			@"{""jsonrpc"":""2.0"",""method"":""getBlockHeight"",""params"":[""main""],""id"":99}");
		var text = JsonConvert.DeserializeObject<RpcRequest>(
			@"{""jsonrpc"":""2.0"",""method"":""getBlockHeight"",""params"":[""main""],""id"":""abc""}");
		var nullId = JsonConvert.DeserializeObject<RpcRequest>(
			@"{""jsonrpc"":""2.0"",""method"":""getBlockHeight"",""params"":[""main""],""id"":null}");
		var omittedId = JsonConvert.DeserializeObject<RpcRequest>(
			@"{""jsonrpc"":""2.0"",""method"":""getBlockHeight"",""params"":[""main""]}");

		numeric.id.ShouldNotBeNull();
		numeric.id!.Value.Type.ShouldBe(JTokenType.Integer);
		numeric.id.Value.Value<long>().ShouldBe(99);

		text.id.ShouldNotBeNull();
		text.id!.Value.Type.ShouldBe(JTokenType.String);
		text.id.Value.Value<string>().ShouldBe("abc");

		nullId.id.ShouldBeNull();
		omittedId.id.ShouldBeNull();
	}

	[Fact]
	public void RpcResponse_Serializes_Explicit_Integer_String_And_Null_Ids()
	{
		JsonConvert.SerializeObject(new RpcResponse(99, true, null))
			.ShouldContain(@"""id"":99");

		JsonConvert.SerializeObject(new RpcResponse("abc", true, null))
			.ShouldContain(@"""id"":""abc""");

		JsonConvert.SerializeObject(new RpcResponse(null, null, new RpcError(RpcErrors.RPC_ERROR_PARSE, "Parse error")))
			.ShouldContain(@"""id"":null");
	}

	[Fact]
	public void RpcResponse_Does_Not_Expose_Old_Implicit_Id_Constructor()
	{
		var oldConstructor = typeof(RpcResponse).GetConstructor(new[] { typeof(object), typeof(RpcError) });

		oldConstructor.ShouldBeNull();
	}

	[Theory]
	[InlineData(@"{""jsonrpc"":""2.0"",""method"":""x"",""params"":[],""id"":{}}")]
	[InlineData(@"{""jsonrpc"":""2.0"",""method"":""x"",""params"":[],""id"":[]}")]
	[InlineData(@"{""jsonrpc"":""2.0"",""method"":""x"",""params"":[],""id"":1.5}")]
	public void RpcRequest_Rejects_Invalid_Id_Types(string json)
	{
		Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<RpcRequest>(json));
	}
}

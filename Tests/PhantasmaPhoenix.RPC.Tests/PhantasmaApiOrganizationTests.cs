using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.RPC.Types;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.RPC.Tests;

public class PhantasmaApiOrganizationTests
{
	[Fact]
	public async Task OrganizationMethods_Send_Name_First_And_Explicit_Carbon_Id_Rpc_Parameters()
	{
		using var handler = new CapturingHandler();
		using var rpcClient = new RpcClient(new HttpClient(handler));
		using var api = new PhantasmaAPI("http://localhost/rpc", rpcClient);

		await api.GetOrganizationAsync("masters", includeMemberCount: true);
		await api.GetOrganizationByNameAsync("validators", includeMemberCount: true);
		await api.GetOrganizationByCarbonIdAsync(1, includeMemberCount: true);
		await api.GetOrganizationsAsync(pageSize: 25, cursor: "ORG_CURSOR", includeMemberCount: true);

		handler.Requests.Count.ShouldBe(4);

		AssertRequest(handler.Requests[0], "getOrganizationByName", parameters =>
		{
			parameters[0]!.Value<string>().ShouldBe("masters");
			parameters[1]!.Value<bool>().ShouldBeTrue();
		});
		AssertRequest(handler.Requests[1], "getOrganizationByName", parameters =>
		{
			parameters[0]!.Value<string>().ShouldBe("validators");
			parameters[1]!.Value<bool>().ShouldBeTrue();
		});
		AssertRequest(handler.Requests[2], "getOrganization", parameters =>
		{
			parameters[0]!.Value<ulong>().ShouldBe(1UL);
			parameters[1]!.Value<bool>().ShouldBeTrue();
		});
		AssertRequest(handler.Requests[3], "getOrganizations", parameters =>
		{
			parameters[0]!.Value<uint>().ShouldBe(25U);
			parameters[1]!.Value<string>().ShouldBe("ORG_CURSOR");
			parameters[2]!.Value<bool>().ShouldBeTrue();
		});
	}

	[Fact]
	public async Task OrganizationMemberMethods_Resolve_String_Organization_Id_To_Carbon_Id()
	{
		using var handler = new CapturingHandler();
		using var rpcClient = new RpcClient(new HttpClient(handler));
		using var api = new PhantasmaAPI("http://localhost/rpc", rpcClient);

		await api.GetOrganizationMembersAsync("masters", pageSize: 50, cursor: "MEMBER_CURSOR", includeMemberTime: false);
		await api.GetOrganizationMemberAsync("validators", "001122", checkAddressReservedByte: false, RpcAddressType.Carbon);

		handler.Requests.Count.ShouldBe(4);

		AssertRequest(handler.Requests[0], "getOrganizationByName", parameters =>
		{
			parameters[0]!.Value<string>().ShouldBe("masters");
			parameters[1]!.Value<bool>().ShouldBeFalse();
		});
		AssertRequest(handler.Requests[1], "getOrganizationMembers", parameters =>
		{
			parameters[0]!.Value<ulong>().ShouldBe(1UL);
			parameters[1]!.Value<uint>().ShouldBe(50U);
			parameters[2]!.Value<string>().ShouldBe("MEMBER_CURSOR");
			parameters[3]!.Value<bool>().ShouldBeFalse();
		});
		AssertRequest(handler.Requests[2], "getOrganizationByName", parameters =>
		{
			parameters[0]!.Value<string>().ShouldBe("validators");
			parameters[1]!.Value<bool>().ShouldBeFalse();
		});
		AssertRequest(handler.Requests[3], "getOrganizationMember", parameters =>
		{
			parameters[0]!.Value<ulong>().ShouldBe(1UL);
			parameters[1]!.Value<string>().ShouldBe("001122");
			parameters[2]!.Value<bool>().ShouldBeFalse();
			parameters[3]!.Value<string>().ShouldBe(nameof(RpcAddressType.Carbon));
		});
	}

	[Fact]
	public async Task OrganizationMemberMethods_Send_Explicit_Carbon_Id_Rpc_Parameters()
	{
		using var handler = new CapturingHandler();
		using var rpcClient = new RpcClient(new HttpClient(handler));
		using var api = new PhantasmaAPI("http://localhost/rpc", rpcClient);

		await api.GetOrganizationMembersByCarbonIdAsync(1);
		await api.GetOrganizationMemberByCarbonIdAsync(1, "Pmember");

		handler.Requests.Count.ShouldBe(2);

		AssertRequest(handler.Requests[0], "getOrganizationMembers", parameters =>
		{
			parameters[0]!.Value<ulong>().ShouldBe(1UL);
			parameters[1]!.Value<uint>().ShouldBe(10U);
			parameters[2]!.Value<string>().ShouldBe(string.Empty);
			parameters[3]!.Value<bool>().ShouldBeTrue();
		});
		AssertRequest(handler.Requests[1], "getOrganizationMember", parameters =>
		{
			parameters[0]!.Value<ulong>().ShouldBe(1UL);
			parameters[1]!.Value<string>().ShouldBe("Pmember");
			parameters[2]!.Value<bool>().ShouldBeTrue();
			parameters[3]!.Value<string>().ShouldBe(nameof(RpcAddressType.Phantasma));
		});
	}

	private static void AssertRequest(JObject request, string method, Action<JArray> assertParameters)
	{
		request["method"]!.Value<string>().ShouldBe(method);
		var parameters = request["params"].ShouldBeOfType<JArray>();
		assertParameters(parameters);
	}

	private sealed class CapturingHandler : HttpMessageHandler
	{
		public List<JObject> Requests { get; } = new();

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var requestJson = request.Content == null
				? string.Empty
				: await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			var requestObject = JObject.Parse(requestJson);
			Requests.Add(requestObject);

			var response = new JObject
			{
				["jsonrpc"] = "2.0",
				["id"] = requestObject["id"]!.DeepClone(),
				["result"] = CreateResult(requestObject["method"]!.Value<string>()!)
			};

			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(response.ToString(Formatting.None), Encoding.UTF8, "application/json")
			};
		}

		private static JToken CreateResult(string method)
		{
			return method switch
			{
				"getOrganization" or "getOrganizationByName" => CreateOrganization("1", "masters"),
				"getOrganizations" => new JObject
				{
					["result"] = new JArray(CreateOrganization("1", "masters")),
					["cursor"] = JValue.CreateNull()
				},
				"getOrganizationMembers" => new JObject
				{
					["result"] = new JArray(CreateMember(true)),
					["cursor"] = JValue.CreateNull()
				},
				"getOrganizationMember" => CreateMember(true),
				_ => JValue.CreateNull()
			};
		}

		private static JObject CreateOrganization(string id, string name)
		{
			return new JObject
			{
				["id"] = id,
				["name"] = name,
				["owner"] = "Powner",
				["carbonOwner"] = "abcdef",
				["metadata"] = new JArray(),
				["memberCount"] = JValue.CreateNull()
			};
		}

		private static JObject CreateMember(bool isMember)
		{
			return new JObject
			{
				["address"] = "Pmember",
				["carbonAddress"] = "abcdef",
				["isMember"] = isMember,
				["memberTime"] = isMember ? new JValue(1743520000000) : JValue.CreateNull()
			};
		}
	}
}

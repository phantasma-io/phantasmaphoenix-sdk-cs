using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.RPC.Types;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.RPC.Tests;

public class PhantasmaApiAccountInfoTests
{
	[Fact]
	public async Task GetAccountInfo_Sends_Account_First_Rpc_Parameters()
	{
		using var handler = new CapturingHandler();
		using var rpcClient = new RpcClient(new HttpClient(handler));
		using var api = new PhantasmaAPI("http://localhost/rpc", rpcClient);

		await api.GetAccountInfoAsync("Paccount");
		await api.GetAccountInfoAsync("001122", checkAddressReservedByte: false, RpcAddressType.Carbon);

		handler.Requests.Count.ShouldBe(2);

		AssertRequest(handler.Requests[0], "getAccountInfo", parameters =>
		{
			parameters.Count.ShouldBe(1);
			parameters[0]!.Value<string>().ShouldBe("Paccount");
		});
		AssertRequest(handler.Requests[1], "getAccountInfo", parameters =>
		{
			parameters[0]!.Value<string>().ShouldBe("001122");
			parameters[1]!.Value<bool>().ShouldBeFalse();
			parameters[2]!.Value<string>().ShouldBe(nameof(RpcAddressType.Carbon));
		});
	}

	// The node names the staking object "stake" on this endpoint, while the legacy getAccount response
	// carries the same object as "stakes". Binding the wrong one silently yields an empty stake, so the
	// mapping is asserted against the exact wire shape the node returns.
	[Fact]
	public async Task GetAccountInfo_Maps_Wire_Shape_To_Model()
	{
		using var handler = new CapturingHandler();
		using var rpcClient = new RpcClient(new HttpClient(handler));
		using var api = new PhantasmaAPI("http://localhost/rpc", rpcClient);

		var info = await api.GetAccountInfoAsync("Paccount");

		info.ShouldNotBeNull();
		info!.Address.ShouldBe("Paccount");
		info.Name.ShouldBe("myname");
		info.Stake.Amount.ShouldBe("1500000000000");
		info.Stake.Time.ShouldBe(1743520000U);
		info.Stake.Unclaimed.ShouldBe("42000000000");
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
				// Shape copied from a live node response to getAccountInfo.
				"getAccountInfo" => new JObject
				{
					["address"] = "Paccount",
					["name"] = "myname",
					["stake"] = new JObject
					{
						["amount"] = "1500000000000",
						["time"] = 1743520000,
						["unclaimed"] = "42000000000"
					}
				},
				_ => JValue.CreateNull()
			};
		}
	}
}

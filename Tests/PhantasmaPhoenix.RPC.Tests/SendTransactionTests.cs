using System.Net;
using System.Numerics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.RPC.Tests;

// The one-step send: pre-flight, plan, sign, broadcast - against a node stubbed at the HTTP level,
// so the real JSON-RPC client, the real decoding and the real error path are all in the loop. The
// broadcast is recorded and decoded back, so what was sent is asserted, not what was intended.
public class SendTransactionTests
{
	private const string MainnetGasConfigJson = """
	{
	  "gasModelVersion": 2,
	  "blockRateTarget": 2000,
	  "expiryWindow": 3600000,
	  "unitsPerBlockDataByte": 25,
	  "gasConfig": {
	    "version": 1, "maxNameLength": 255, "maxTokenSymbolLength": 255, "feeShift": 0, "maxStructureSize": 1048576,
	    "feeMultiplier": "10000", "gasTokenId": "1", "dataTokenId": "2", "minimumGasOffer": "10", "dataEscrowPerRow": "200000",
	    "gasFeeTransfer": "10", "gasFeeQuery": "10", "gasFeeCreateTokenBase": "10000000000", "gasFeeCreateTokenSymbol": "10000000000",
	    "gasFeeCreateTokenSeries": "2500000000", "gasFeePerByte": "250000", "gasFeeRegisterName": "10000000000000",
	    "gasBurnRatioMul": "1", "gasBurnRatioShift": 0, "minimumGasBill": "10000000",
	    "gasProducerRatioMul": "0", "gasProducerRatioShift": 0, "gasDappRatioMul": "0", "gasDappRatioShift": 0,
	    "policyFeeCreateTokenBase": "100000000000000", "policyFeeCreateTokenSymbol": "100000000000000",
	    "policyFeeCreateTokenSeries": "25000000000000", "policyFeeRegisterName": "100000000000000000", "legacyDataEscrowPerRow": "2"
	  }
	}
	""";

	// A node whose answers are canned: the gas config, the token lookups a pre-flight makes, and the
	// broadcast, which records what it was given. Absent symbols are answered the way the node does
	// it - an HTTP error status carrying a JSON-RPC error body.
	private sealed class StubNode : HttpMessageHandler
	{
		public List<string> Sent { get; } = new();
		public HashSet<string> Tokens { get; } = new();
		public int Lookups { get; private set; }
		/// <summary>False simulates a node that cannot serve getToken at all, not even by id.</summary>
		public bool Reachable { get; set; } = true;
		public string LookupError { get; set; } = "Token symbol not found";
		public string? BroadcastError { get; set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var body = JObject.Parse(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
			var id = body["id"]!.DeepClone();
			var method = body["method"]!.Value<string>();
			var parameters = (JArray)body["params"]!;
			switch (method)
			{
				case "getGasConfig":
					return Ok(id, JObject.Parse(MainnetGasConfigJson));
				case "getToken":
					{
						Lookups++;
						var symbol = parameters[0]!.Value<string>() ?? string.Empty;
						var byId = parameters.Count > 2 ? parameters[2]!.Value<ulong>() : 0;
						if (!Reachable) return Error(id, LookupError);
						// A live node resolves by id without looking at the symbol; that path is the pre-flight's
						// control, and it answers for the gas token whatever the caller's symbol turns out to be.
						if (byId != 0) return Ok(id, new JObject { ["symbol"] = "KCAL" });
						return Tokens.Contains(symbol) ? Ok(id, new JObject { ["symbol"] = symbol }) : Error(id, LookupError);
					}
				case "sendCarbonTransaction":
					Sent.Add(parameters[0]!.Value<string>()!);
					return BroadcastError != null ? Error(id, BroadcastError) : Ok(id, "HASH");
				default:
					return Error(id, $"unexpected method {method}");
			}
		}

		private static HttpResponseMessage Ok(JToken id, JToken result) => Respond(HttpStatusCode.OK,
			new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });

		private static HttpResponseMessage Error(JToken id, string message) => Respond(HttpStatusCode.BadRequest,
			new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JObject { ["code"] = -32603, ["message"] = message } });

		private static HttpResponseMessage Respond(HttpStatusCode status, JObject envelope) => new(status)
		{
			Content = new StringContent(envelope.ToString(Formatting.None), Encoding.UTF8, "application/json")
		};
	}

	private static readonly PhantasmaKeys Owner = PhantasmaKeys.FromWIF("KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d");
	private static readonly PhantasmaKeys Payer = PhantasmaKeys.FromWIF("KwVG94yjfVg1YKFyRxAGtug93wdRbmLnqqrFV6Yd2CiA9KZDAp4H");
	private static readonly Bytes32 OwnerPub = new(Owner.PublicKey);
	private static readonly Bytes32 PayerPub = new(Payer.PublicKey);

	private static (PhantasmaAPI api, StubNode node) Api()
	{
		var node = new StubNode();
		var api = new PhantasmaAPI("http://localhost/rpc", new RpcClient(new HttpClient(node)));
		return (api, node);
	}

	private static TxMsg Transfer(Bytes32? gasPayer = null, ulong maxGas = 0) =>
		NativeTxHelper.TransferFungible(new TransferFungibleParams { From = OwnerPub, To = PayerPub, TokenId = 1, Amount = 5, GasPayer = gasPayer, MaxGas = maxGas == 0 ? null : maxGas });

	private static TxMsg CreateToken(string symbol)
	{
		var info = TokenInfoBuilder.Build(symbol, new IntX(0), false, 2, OwnerPub,
			TokenMetadataBuilder.BuildAndSerialize(new Dictionary<string, string>
			{
				["name"] = "Send probe",
				["icon"] = "data:image/png;base64,iVBORw0KGgo=",
				["url"] = "https://example.invalid/p",
				["description"] = "x"
			}), null);
		return CreateTokenTxHelper.BuildTx(info, OwnerPub);
	}

	private static TxMsg RegisterName(string name)
	{
		using var stream = new MemoryStream();
		using var w = new BinaryWriter(stream);
		w.Write32(OwnerPub);
		w.Write(new SmallString(name));
		return new TxMsg
		{
			type = TxTypes.Call,
			expiry = 1_787_000_000_000,
			maxGas = 0,
			maxData = 0,
			gasFrom = OwnerPub,
			payload = SmallString.Empty,
			msg = new TxMsgCall { moduleId = (uint)ModuleId.Governance, methodId = (uint)GovernanceContract_Methods.RegisterName, args = stream.ToArray() }
		};
	}

	private static SignedTxMsg DecodeSent(StubNode node) => CarbonBlob.New<SignedTxMsg>(Base16.Decode(node.Sent[0])!);

	[Fact]
	public async Task Plans_an_unplanned_message_signs_it_and_broadcasts_the_envelope()
	{
		var (api, node) = Api();
		var hash = await api.SendTransactionAsync(Transfer(), Owner);

		hash.ShouldBe("HASH");
		node.Sent.Count.ShouldBe(1);
		var sent = DecodeSent(node);
		sent.msg.maxGas.ShouldBe(42_600_000UL);
		sent.witnesses.Length.ShouldBe(1);
		sent.witnesses[0].address.ShouldBe(OwnerPub);
		// Nothing was looked up: a transfer has no pre-flight.
		node.Lookups.ShouldBe(0);
	}

	[Fact]
	public async Task Sends_a_message_the_caller_already_planned_as_it_is()
	{
		var (api, node) = Api();
		await api.SendTransactionAsync(Transfer(maxGas: 999_000_000), Owner);
		DecodeSent(node).msg.maxGas.ShouldBe(999_000_000UL);
	}

	[Fact]
	public async Task Collects_every_witness_of_a_gas_payer_transfer()
	{
		var (api, node) = Api();
		await api.SendTransactionAsync(Transfer(gasPayer: PayerPub), new IKeyPair[] { Owner, Payer });
		var sent = DecodeSent(node);
		sent.msg.type.ShouldBe(TxTypes.TransferFungible_GasPayer);
		sent.witnesses.Select(w => w.address).ShouldBe(new[] { PayerPub, OwnerPub });
		// Two bare signatures: the envelope the plan was priced for.
		Base16.Decode(node.Sent[0])!.Length.ShouldBe(106 + 32 + 128);
	}

	[Fact]
	public async Task Refuses_to_create_a_token_whose_symbol_is_taken()
	{
		var (api, node) = Api();
		node.Tokens.Add("KCAL");
		var error = await Should.ThrowAsync<TransactionPreflightException>(() => api.SendTransactionAsync(CreateToken("KCAL"), Owner));
		error.Message.ShouldBe("token symbol KCAL is already taken");
		node.Sent.ShouldBeEmpty();
	}

	[Fact]
	public async Task Sends_a_name_registration_without_looking_anything_up()
	{
		var (api, node) = Api();
		await api.SendTransactionAsync(RegisterName("alice"), Owner);
		node.Lookups.ShouldBe(0);
		node.Sent.Count.ShouldBe(1);
	}

	[Fact]
	public async Task Establishes_a_free_symbol_from_a_control_lookup_not_from_the_error_text()
	{
		var (api, node) = Api();
		await api.SendTransactionAsync(CreateToken("GPX"), Owner);
		// The symbol lookup failed, so the gas token was fetched by id as the control: two lookups.
		node.Lookups.ShouldBe(2);
		node.Sent.Count.ShouldBe(1);
		DecodeSent(node).msg.maxGas.ShouldBeGreaterThan(100_000_000_000_000UL);
	}

	[Fact]
	public async Task Refuses_when_the_lookup_cannot_answer_even_about_a_token_that_exists()
	{
		var (api, node) = Api();
		node.Reachable = false;
		node.LookupError = "backend unavailable";
		var error = await Should.ThrowAsync<TransactionPreflightException>(() => api.SendTransactionAsync(CreateToken("GPX"), Owner));
		error.Message.ShouldContain("Could not establish whether token symbol GPX is taken");
		error.Message.ShouldContain("backend unavailable");
		node.Sent.ShouldBeEmpty();
	}

	[Fact]
	public async Task Hands_the_verdict_to_a_caller_who_wants_to_decide_for_itself()
	{
		var (api, node) = Api();
		node.Reachable = false;
		var verdict = await TransactionPreflight.PreflightAsync(api, CreateToken("GPX"));
		verdict.Verdict.ShouldBe(PreflightVerdict.Unknown);
		verdict.Subject.ShouldBe("token symbol GPX");
		verdict.Reason.ShouldNotBeNullOrEmpty();

		(await TransactionPreflight.PreflightAsync(api, Transfer())).Verdict.ShouldBe(PreflightVerdict.NotApplicable);

		// Having read the verdict, the caller may still send - with the pre-flight off.
		await api.SendTransactionAsync(CreateToken("GPX"), Owner, new SendTransactionOptions { Preflight = false });
		node.Sent.Count.ShouldBe(1);
	}

	[Fact]
	public async Task Sends_a_gas_payer_transfer_whose_payer_and_owner_are_the_same_account()
	{
		var (api, node) = Api();
		var msg = NativeTxHelper.TransferFungible(new TransferFungibleParams { From = OwnerPub, To = PayerPub, TokenId = 1, Amount = 5, GasPayer = OwnerPub });
		await api.SendTransactionAsync(msg, Owner);
		var sent = DecodeSent(node);
		sent.witnesses.Length.ShouldBe(2);
		sent.witnesses[0].signature.ShouldBe(sent.witnesses[1].signature);
	}

	[Fact]
	public async Task Surfaces_a_broadcast_rejection_as_an_error()
	{
		var (api, node) = Api();
		node.BroadcastError = "below minimum gas";
		var error = await Should.ThrowAsync<Exception>(() => api.SendTransactionAsync(Transfer(), Owner));
		error.Message.ShouldContain("below minimum gas");
	}

	[Fact]
	public async Task Plans_a_call_for_as_many_witnesses_as_it_is_given()
	{
		var (api, node) = Api();
		await api.SendTransactionAsync(RegisterName("bob"), new IKeyPair[] { Owner, Payer });
		var sent = DecodeSent(node);
		sent.witnesses.Length.ShouldBe(2);
		// The planned envelope is the sent envelope: two witness-array entries of 96 bytes each.
		var plan = FeePlanner.Plan(RegisterName("bob"), (await api.Fees.ConfigAsync()), new FeePlanOptions { WitnessCount = 2 });
		Base16.Decode(node.Sent[0])!.Length.ShouldBe(plan.EnvelopeBytes);
		sent.msg.maxGas.ShouldBe(plan.MaxGas);
	}
}

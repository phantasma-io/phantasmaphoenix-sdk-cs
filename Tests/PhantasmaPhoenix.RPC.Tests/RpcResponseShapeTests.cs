using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.RPC.Types;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.RPC.Tests;

public class RpcResponseShapeTests
{
	private static readonly JsonSerializerSettings SerializerSettings = new()
	{
		ContractResolver = new DefaultContractResolver { NamingStrategy = new DefaultNamingStrategy() },
		NullValueHandling = NullValueHandling.Ignore,
		MissingMemberHandling = MissingMemberHandling.Ignore,
		Converters = { new StringEnumConverter(new DefaultNamingStrategy(), allowIntegerValues: true) }
	};

	[Fact]
	public void RpcDtos_Decode_Current_Response_Shapes()
	{
		var tx = Decode<TransactionResult>(
			"""
			{
			  "hash": "HASH",
			  "chainAddress": "CHAIN",
			  "timestamp": 123,
			  "blockHeight": 456,
			  "blockHash": "BLOCK",
			  "script": "",
			  "payload": "CAFE",
			  "carbonTxType": 9,
			  "carbonTxData": "BEEF",
			  "debugComment": "mint",
			  "events": [{"address": "Pevent", "contract": "gas", "kind": "GasEscrow", "name": "GasEscrow", "data": "00"}],
			  "extendedEvents": [{"address": "Pevent", "contract": "market", "kind": "TokenCreate", "data": {"symbol": "CROWN"}}],
			  "state": "Halt",
			  "result": "",
			  "fee": "2600000",
			  "signatures": [{"kind": "Ed25519", "data": "AA"}],
			  "sender": "Psender",
			  "gasPayer": "Pgas",
			  "gasTarget": "Ptarget",
			  "gasPrice": "1",
			  "gasLimit": "100000000",
			  "expiration": 789
			}
			""");

		tx.CarbonTxType.ShouldBe((byte)9);
		tx.CarbonTxData.ShouldBe("BEEF");
		tx.DebugComment.ShouldBe("mint");
		tx.Sender.ShouldBe("Psender");
		tx.GasPayer.ShouldBe("Pgas");
		tx.GasTarget.ShouldBe("Ptarget");
		tx.GasPrice.ShouldBe("1");
		tx.GasLimit.ShouldBe("100000000");
		tx.Events[0].Name.ShouldBe("GasEscrow");
		tx.ExtendedEvents[0].Kind.ShouldBe(EventKind.TokenCreate);
		tx.Signatures![0].Kind.ShouldBe("Ed25519");
		tx.Signatures[0].Data.ShouldBe("AA");

		var block = Decode<BlockResult>(
			"""
			{"hash":"BLOCK","height":456,"txs":[],"reward":"0"}
			""");
		block.Events.ShouldBeNull();
		block.Oracles.ShouldBeNull();

		var token = Decode<TokenResult>(
			"""
			{
			  "symbol":"CROWN",
			  "name":"Crown",
			  "decimals":0,
			  "currentSupply":"1",
			  "maxSupply":"0",
			  "burnedSupply":"0",
			  "address":"S-token",
			  "owner":"Powner",
			  "flags":"Transferable, NonFungible",
			  "carbonId":"4",
			  "metadata":[{"key":"name","value":"Crown"}],
			  "series":[{"seriesId":"0","carbonTokenId":"4","carbonSeriesId":"1"}]
			}
			""");
		token.CarbonId.ShouldBe("4");
		token.Metadata![0].Key.ShouldBe("name");
		token.Metadata[0].Value.ShouldBe("Crown");
		token.Series[0].SeriesId.ShouldBe("0");
		token.Series[0].carbonSeriesId.ShouldBe("1");
		token.Script.ShouldBeNull();
		token.External.ShouldBeNull();
		token.Price.ShouldBeNull();

		var nft = Decode<TokenDataResult>(
			"""
			{
			  "id":"114421",
			  "series":"0",
			  "carbonTokenId":"4",
			  "carbonSeriesId":"1",
			  "carbonNftAddress":"ABCDEF",
			  "mint":"1",
			  "chainName":"main",
			  "ownerAddress":"Powner",
			  "creatorAddress":"Pcreator",
			  "ram":"",
			  "rom":"CAFE",
			  "status":"Active",
			  "infusion":[],
			  "properties":[{"key":"name","value":"Crown #1"}]
			}
			""");
		nft.Id.ShouldBe("114421");
		nft.Series.ShouldBe("0");
		nft.carbonSeriesId.ShouldBe("1");
		nft.Properties[0].Value.ShouldBe("Crown #1");

		var chain = Decode<ChainResult>("""{"height":0}""");
		chain.Name.ShouldBeNull();
		chain.Contracts.ShouldBeNull();

		var archive = Decode<ArchiveResult>("""{"time":0,"size":0,"blockCount":0}""");
		archive.Name.ShouldBeNull();
		archive.MissingBlocks.ShouldBeNull();

		var script = Decode<ScriptResult>("""{"events":[],"result":"0601","results":["0601"],"oracles":[]}""");
		script.Error.ShouldBeNull();
		script.State.ShouldBeNull();
		script.Gas.ShouldBeNull();

		var organization = Decode<OrganizationResult>(
			"""
			{
			  "id":"1",
			  "name":"masters",
			  "owner":"Powner",
			  "carbonOwner":"00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff",
			  "metadata":[{"key":"role","value":"validators"}],
			  "memberCount":"1106"
			}
			""");
		organization.Id.ShouldBe("1");
		organization.Name.ShouldBe("masters");
		organization.Owner.ShouldBe("Powner");
		organization.CarbonOwner.ShouldBe("00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff");
		organization.Metadata[0].Key.ShouldBe("role");
		organization.Metadata[0].Value.ShouldBe("validators");
		organization.MemberCount.ShouldBe("1106");

		var organizationPage = Decode<CursorPaginatedResult<OrganizationResult[]>>(
			"""
			{
			  "result":[{"id":"2","name":"validators","owner":"Powner","carbonOwner":"abcdef","metadata":[]}],
			  "cursor":"NEXT"
			}
			""");
		organizationPage.Result![0].Id.ShouldBe("2");
		organizationPage.Result[0].Name.ShouldBe("validators");
		organizationPage.Cursor.ShouldBe("NEXT");

		var organizationMember = Decode<OrganizationMemberResult>(
			"""
			{
			  "address":"Pmember",
			  "carbonAddress":"abcdef",
			  "isMember":true,
			  "memberTime":1743520000000
			}
			""");
		organizationMember.Address.ShouldBe("Pmember");
		organizationMember.CarbonAddress.ShouldBe("abcdef");
		organizationMember.IsMember.ShouldBeTrue();
		organizationMember.MemberTime.ShouldBe(1743520000000);

		var nonMember = Decode<OrganizationMemberResult>(
			"""
			{"address":"Pmissing","carbonAddress":"fedcba","isMember":false}
			""");
		nonMember.IsMember.ShouldBeFalse();
		nonMember.MemberTime.ShouldBeNull();
	}

	[Fact]
	public void RpcDtos_Ignore_Stale_Wire_Field_Names_Without_Alias_Mapping()
	{
		var signature = Decode<TransactionSignatureResult>("""{"Kind":"Ed25519","Data":"AA"}""");
		signature.Kind.ShouldBe(string.Empty);
		signature.Data.ShouldBe(string.Empty);

		var currentSignature = Decode<TransactionSignatureResult>("""{"kind":"Ed25519","data":"AA","Kind":"Wrong","Data":"Wrong"}""");
		currentSignature.Kind.ShouldBe("Ed25519");
		currentSignature.Data.ShouldBe("AA");

		var eventResult = Decode<EventResult>(
			"""{"address":"Pevent","contract":"gas","Kind":"GasEscrow","name":"GasEscrow","Data":"00"}""");
		eventResult.Kind.ShouldBe(string.Empty);
		eventResult.Data.ShouldBe(string.Empty);

		var extendedEvent = Decode<EventExResult>(
			"""{"address":"Pevent","contract":"market","Kind":"TokenCreate","Data":{"symbol":"CROWN"}}""");
		extendedEvent.Kind.ShouldBe(default);
		extendedEvent.Data.ShouldBeNull();

		var property = Decode<TokenPropertyResult>("""{"Key":"name","Value":"Crown"}""");
		property.Key.ShouldBe(string.Empty);
		property.Value.ShouldBe(string.Empty);

		var nft = Decode<TokenDataResult>(
			"""
			{
			  "ID":"114421",
			  "series":"0",
			  "carbonTokenId":"4",
			  "carbonSeriesId":"1",
			  "carbonNftAddress":"ABCDEF",
			  "mint":"1",
			  "chainName":"main",
			  "ownerAddress":"Powner",
			  "creatorAddress":"Pcreator",
			  "ram":"",
			  "rom":"CAFE",
			  "status":"Active",
			  "infusion":[],
			  "properties":[]
			}
			""");
		nft.Id.ShouldBe(string.Empty);
		nft.Series.ShouldBe("0");

		var token = Decode<TokenResult>(
			"""
			{
			  "symbol":"CROWN",
			  "name":"Crown",
			  "decimals":0,
			  "currentSupply":"1",
			  "maxSupply":"0",
			  "burnedSupply":"0",
			  "address":"S-token",
			  "owner":"Powner",
			  "flags":"Transferable, NonFungible",
			  "carbonID":"4",
			  "metadata":[],
			  "series":[]
			}
			""");
		token.Symbol.ShouldBe("CROWN");
		token.CarbonId.ShouldBe(string.Empty);

		typeof(OrganizationResult).GetProperty("Members").ShouldBeNull();
	}

	private static T Decode<T>(string json)
	{
		return JsonConvert.DeserializeObject<T>(json, SerializerSettings)!;
	}

}

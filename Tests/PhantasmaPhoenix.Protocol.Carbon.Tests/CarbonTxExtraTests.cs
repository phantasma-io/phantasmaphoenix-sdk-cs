using System.Numerics;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

public class CarbonTxExtraTests
{
	private const string TransferFungibleGasPayerHex =
		"04C04EF9B6990100008096980000000000E803000000000000F94A8E45BDF1E37A8466B951849E92D1BAF870F49D1D04CD204D0BC9FE4308960C746573742D7061796C6F6164D4C5061B81C4682B27A0CFC6459CD9D7892EB60A43F73DD1060B6C478AA7C3D8F94A8E45BDF1E37A8466B951849E92D1BAF870F49D1D04CD204D0BC9FE430896010000000000000000E1F50500000000";
	private const string BurnFungibleGasPayerHex =
		"0BC04EF9B6990100008096980000000000E803000000000000F94A8E45BDF1E37A8466B951849E92D1BAF870F49D1D04CD204D0BC9FE4308960C746573742D7061796C6F61640100000000000000F94A8E45BDF1E37A8466B951849E92D1BAF870F49D1D04CD204D0BC9FE4308960800E1F50500000000";
	private const string MintFungibleHex =
		"09C04EF9B6990100008096980000000000E803000000000000F94A8E45BDF1E37A8466B951849E92D1BAF870F49D1D04CD204D0BC9FE4308960C746573742D7061796C6F61640100000000000000D4C5061B81C4682B27A0CFC6459CD9D7892EB60A43F73DD1060B6C478AA7C3D80800E1F50500000000";

	private const string SenderWif = "KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d";
	private const string ReceiverWif = "KwVG94yjfVg1YKFyRxAGtug93wdRbmLnqqrFV6Yd2CiA9KZDAp4H";

	[Fact]
	public void TransferFungible_gas_payer_vector_roundtrip()
	{
		var (senderPub, receiverPub) = GetSenderReceiver();
		var tx = new TxMsg
		{
			type = TxTypes.TransferFungible_GasPayer,
			expiry = 1759711416000,
			maxGas = 10000000,
			maxData = 1000,
			gasFrom = senderPub,
			payload = new SmallString("test-payload"),
			msg = new TxMsgTransferFungible_GasPayer
			{
				to = receiverPub,
				from = senderPub,
				tokenId = 1,
				amount = 100000000
			}
		};

		CarbonBlob.Serialize(tx).ToHex().ShouldBe(TransferFungibleGasPayerHex);

		var decoded = CarbonBlob.New<TxMsg>(TransferFungibleGasPayerHex.FromHex()!);
		decoded.type.ShouldBe(TxTypes.TransferFungible_GasPayer);
		decoded.expiry.ShouldBe(1759711416000);
		decoded.maxGas.ShouldBe(10000000UL);
		decoded.maxData.ShouldBe(1000UL);
		decoded.gasFrom.Equals(senderPub).ShouldBeTrue();
		decoded.payload.data.ShouldBe("test-payload");

		var msg = (TxMsgTransferFungible_GasPayer)decoded.msg;
		msg.tokenId.ShouldBe(1UL);
		msg.amount.ShouldBe(100000000UL);
		msg.to.Equals(receiverPub).ShouldBeTrue();
		msg.from.Equals(senderPub).ShouldBeTrue();

		CarbonBlob.Serialize(decoded).ToHex().ShouldBe(TransferFungibleGasPayerHex);
	}

	[Fact]
	public void BurnFungible_gas_payer_vector_roundtrip()
	{
		var (senderPub, _) = GetSenderReceiver();
		var tx = new TxMsg
		{
			type = TxTypes.BurnFungible_GasPayer,
			expiry = 1759711416000,
			maxGas = 10000000,
			maxData = 1000,
			gasFrom = senderPub,
			payload = new SmallString("test-payload"),
			msg = new TxMsgBurnFungible_GasPayer
			{
				tokenId = 1,
				from = senderPub,
				amount = new IntX(100000000)
			}
		};

		CarbonBlob.Serialize(tx).ToHex().ShouldBe(BurnFungibleGasPayerHex);

		var decoded = CarbonBlob.New<TxMsg>(BurnFungibleGasPayerHex.FromHex()!);
		decoded.type.ShouldBe(TxTypes.BurnFungible_GasPayer);
		decoded.expiry.ShouldBe(1759711416000);
		decoded.maxGas.ShouldBe(10000000UL);
		decoded.maxData.ShouldBe(1000UL);
		decoded.gasFrom.Equals(senderPub).ShouldBeTrue();
		decoded.payload.data.ShouldBe("test-payload");

		var msg = (TxMsgBurnFungible_GasPayer)decoded.msg;
		msg.tokenId.ShouldBe(1UL);
		((BigInteger)msg.amount).ShouldBe(new BigInteger(100000000));
		msg.from.Equals(senderPub).ShouldBeTrue();

		CarbonBlob.Serialize(decoded).ToHex().ShouldBe(BurnFungibleGasPayerHex);
	}

	[Fact]
	public void MintFungible_vector_roundtrip()
	{
		var (senderPub, receiverPub) = GetSenderReceiver();
		var tx = new TxMsg
		{
			type = TxTypes.MintFungible,
			expiry = 1759711416000,
			maxGas = 10000000,
			maxData = 1000,
			gasFrom = senderPub,
			payload = new SmallString("test-payload"),
			msg = new TxMsgMintFungible
			{
				tokenId = 1,
				to = receiverPub,
				amount = new IntX(100000000)
			}
		};

		CarbonBlob.Serialize(tx).ToHex().ShouldBe(MintFungibleHex);

		var decoded = CarbonBlob.New<TxMsg>(MintFungibleHex.FromHex()!);
		decoded.type.ShouldBe(TxTypes.MintFungible);
		decoded.expiry.ShouldBe(1759711416000);
		decoded.maxGas.ShouldBe(10000000UL);
		decoded.maxData.ShouldBe(1000UL);
		decoded.gasFrom.Equals(senderPub).ShouldBeTrue();
		decoded.payload.data.ShouldBe("test-payload");

		var msg = (TxMsgMintFungible)decoded.msg;
		msg.tokenId.ShouldBe(1UL);
		((BigInteger)msg.amount).ShouldBe(new BigInteger(100000000));
		msg.to.Equals(receiverPub).ShouldBeTrue();

		CarbonBlob.Serialize(decoded).ToHex().ShouldBe(MintFungibleHex);
	}

	private static (Bytes32 sender, Bytes32 receiver) GetSenderReceiver()
	{
		var sender = PhantasmaKeys.FromWIF(SenderWif);
		var receiver = PhantasmaKeys.FromWIF(ReceiverWif);
		return (new Bytes32(sender.PublicKey), new Bytes32(receiver.PublicKey));
	}
}

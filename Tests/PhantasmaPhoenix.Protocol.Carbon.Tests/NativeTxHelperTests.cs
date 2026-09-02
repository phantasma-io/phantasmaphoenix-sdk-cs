using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

// The native builders assemble the message and nothing else: the type follows from who pays and how
// many instances move, the limits are the caller's or the unplanned defaults, and no price is ever
// invented. Prices come from the planner afterwards.
public class NativeTxHelperTests
{
	private static readonly Bytes32 Owner = new(PhantasmaKeys.FromWIF("KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d").PublicKey);
	private static readonly Bytes32 Payer = new(PhantasmaKeys.FromWIF("KwVG94yjfVg1YKFyRxAGtug93wdRbmLnqqrFV6Yd2CiA9KZDAp4H").PublicKey);
	private static readonly Bytes32 Receiver = new(Enumerable.Repeat((byte)7, 32).ToArray());

	[Fact]
	public void Builds_a_plain_transfer_paid_by_the_sender_unplanned_until_fees_are_set()
	{
		var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var msg = NativeTxHelper.TransferFungible(new TransferFungibleParams { From = Owner, To = Receiver, TokenId = 1, Amount = 5 });

		msg.type.ShouldBe(TxTypes.TransferFungible);
		msg.gasFrom.ShouldBe(Owner);
		msg.maxGas.ShouldBe(0UL);
		msg.maxData.ShouldBe(0UL);
		msg.payload.data.ShouldBe(string.Empty);
		// The default lifetime is the builder's, stamped from now.
		msg.expiry.ShouldBeInRange(before + TxLimits.DefaultExpiryMs, before + TxLimits.DefaultExpiryMs + 5_000);
		var inner = (TxMsgTransferFungible)msg.msg;
		inner.to.ShouldBe(Receiver);
		inner.tokenId.ShouldBe(1UL);
		inner.amount.ShouldBe(5UL);

		// A zero offer is never admitted, so the message cannot be signed until it is planned.
		Should.Throw<InvalidOperationException>(() => TxMsgSigner.SignWithKeys(msg, PhantasmaKeys.Generate()));
	}

	[Fact]
	public void Switches_to_the_gas_payer_form_when_another_account_pays()
	{
		var msg = NativeTxHelper.TransferFungible(new TransferFungibleParams { From = Owner, To = Receiver, TokenId = 1, Amount = 5, GasPayer = Payer });
		msg.type.ShouldBe(TxTypes.TransferFungible_GasPayer);
		// The payer is the gas account of the message. The owner is the `from` inside it.
		msg.gasFrom.ShouldBe(Payer);
		var inner = (TxMsgTransferFungible_GasPayer)msg.msg;
		inner.from.ShouldBe(Owner);
		inner.to.ShouldBe(Receiver);
		SignedTxMsg.RequiredWitnesses(msg).ShouldBe(new[] { Payer, Owner });

		var burn = NativeTxHelper.BurnFungible(new BurnFungibleParams { From = Owner, TokenId = 9, Amount = new IntX(3), GasPayer = Payer });
		burn.type.ShouldBe(TxTypes.BurnFungible_GasPayer);
		((TxMsgBurnFungible_GasPayer)burn.msg).from.ShouldBe(Owner);

		var burnNft = NativeTxHelper.BurnNonFungible(new BurnNonFungibleParams { From = Owner, TokenId = 9, InstanceId = 4, GasPayer = Payer });
		burnNft.type.ShouldBe(TxTypes.BurnNonFungible_GasPayer);
		((TxMsgBurnNonFungible_GasPayer)burnNft.msg).instanceId.ShouldBe(4UL);
	}

	[Fact]
	public void Picks_the_single_or_multi_instance_NFT_transfer_by_the_instance_count()
	{
		var single = NativeTxHelper.TransferNonFungible(new TransferNonFungibleParams { From = Owner, To = Receiver, TokenId = 9, InstanceIds = new ulong[] { 11 } });
		single.type.ShouldBe(TxTypes.TransferNonFungible_Single);
		((TxMsgTransferNonFungible_Single)single.msg).instanceId.ShouldBe(11UL);

		var multi = NativeTxHelper.TransferNonFungible(new TransferNonFungibleParams { From = Owner, To = Receiver, TokenId = 9, InstanceIds = new ulong[] { 11, 12 } });
		multi.type.ShouldBe(TxTypes.TransferNonFungible_Multi);
		((TxMsgTransferNonFungible_Multi)multi.msg).instanceIds.ShouldBe(new ulong[] { 11, 12 });

		var paid = NativeTxHelper.TransferNonFungible(new TransferNonFungibleParams { From = Owner, To = Receiver, TokenId = 9, InstanceIds = new ulong[] { 11, 12 }, GasPayer = Payer });
		paid.type.ShouldBe(TxTypes.TransferNonFungible_Multi_GasPayer);
		((TxMsgTransferNonFungible_Multi_GasPayer)paid.msg).from.ShouldBe(Owner);

		Should.Throw<ArgumentException>(() => NativeTxHelper.TransferNonFungible(new TransferNonFungibleParams { From = Owner, To = Receiver, TokenId = 9 }));
	}

	[Fact]
	public void Builds_mints_and_burns_with_the_limits_given()
	{
		var mint = NativeTxHelper.MintFungible(new MintFungibleParams { Owner = Owner, To = Receiver, TokenId = 9, Amount = new IntX(1000), MaxGas = 123, MaxData = 456, Expiry = 789 });
		mint.type.ShouldBe(TxTypes.MintFungible);
		mint.gasFrom.ShouldBe(Owner);
		mint.maxGas.ShouldBe(123UL);
		mint.maxData.ShouldBe(456UL);
		mint.expiry.ShouldBe(789);
		var inner = (TxMsgMintFungible)mint.msg;
		inner.to.ShouldBe(Receiver);
		((long)inner.amount).ShouldBe(1000);

		var burn = NativeTxHelper.BurnFungible(new BurnFungibleParams { From = Owner, TokenId = 9, Amount = new IntX(1), MaxGas = 5 });
		burn.type.ShouldBe(TxTypes.BurnFungible);
		burn.gasFrom.ShouldBe(Owner);
		burn.maxGas.ShouldBe(5UL);
		((TxMsgBurnFungible)burn.msg).tokenId.ShouldBe(9UL);
	}
}

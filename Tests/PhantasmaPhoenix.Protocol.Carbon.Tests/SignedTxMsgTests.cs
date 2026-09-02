using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

// The envelope layout per transaction type is consensus: a wrong witness set is rejected by the
// node, and the envelope's byte length is what gas model v2 bills. These tests pin the witness set
// each type requires, the size predicted without a key against the size a real signing produces,
// and the mistakes the writer refuses before anything is sent.
public class SignedTxMsgTests
{
	private static readonly PhantasmaKeys Payer = PhantasmaKeys.FromWIF("KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d");
	private static readonly PhantasmaKeys Owner = PhantasmaKeys.FromWIF("KwVG94yjfVg1YKFyRxAGtug93wdRbmLnqqrFV6Yd2CiA9KZDAp4H");
	private static readonly Bytes32 PayerPub = new(Payer.PublicKey);
	private static readonly Bytes32 OwnerPub = new(Owner.PublicKey);

	private static TxMsg Base(TxTypes type, Bytes32 gasFrom, object inner) => new()
	{
		type = type,
		expiry = 1_787_000_000_000,
		maxGas = 42_600_000,
		maxData = 0,
		gasFrom = gasFrom,
		payload = SmallString.Empty,
		msg = inner
	};

	private static TxMsg Transfer() =>
		Base(TxTypes.TransferFungible, PayerPub, new TxMsgTransferFungible { to = OwnerPub, tokenId = 1, amount = 5 });

	private static TxMsg GasPayerTransfer() =>
		Base(TxTypes.TransferFungible_GasPayer, PayerPub,
			new TxMsgTransferFungible_GasPayer { to = PayerPub, from = OwnerPub, tokenId = 1, amount = 5 });

	private static TxMsg Call(byte[]? args = null) =>
		Base(TxTypes.Call, PayerPub, new TxMsgCall
		{
			moduleId = (uint)ModuleId.Governance,
			methodId = 1,
			args = args ?? new byte[40]
		});

	private static Witness Sign(TxMsg msg, PhantasmaKeys keys) => new()
	{
		address = new Bytes32(keys.PublicKey),
		signature = new Bytes64(Ed25519.Sign(CarbonBlob.Serialize(msg), keys.PrivateKey))
	};

	[Fact]
	public void Names_the_witnesses_each_message_type_requires_in_envelope_order()
	{
		SignedTxMsg.RequiredWitnesses(Transfer()).ShouldBe(new[] { PayerPub });
		// The gas payer signs first, then the account whose tokens move.
		SignedTxMsg.RequiredWitnesses(GasPayerTransfer()).ShouldBe(new[] { PayerPub, OwnerPub });
		SignedTxMsg.RequiredWitnesses(Call()).ShouldBeNull();
		SignedTxMsg.RequiredWitnesses(TxMsg.FromPhantasmaRaw(new byte[3])).ShouldBeEmpty();
	}

	[Fact]
	public void Predicts_the_signed_size_without_a_key()
	{
		var transfer = Transfer();
		var signedTransfer = CarbonBlob.Serialize(new SignedTxMsg { msg = transfer, witnesses = new[] { Sign(transfer, Payer) } });
		SignedTxMsg.EnvelopeBytes(transfer).ShouldBe(signedTransfer.Length);
		// 58 header bytes + 48 for the transfer body + one bare 64-byte signature.
		SignedTxMsg.EnvelopeBytes(transfer).ShouldBe(170);

		var gasPayer = GasPayerTransfer();
		var signedGasPayer = CarbonBlob.Serialize(new SignedTxMsg
		{
			msg = gasPayer,
			witnesses = new[] { Sign(gasPayer, Payer), Sign(gasPayer, Owner) }
		});
		SignedTxMsg.EnvelopeBytes(gasPayer).ShouldBe(signedGasPayer.Length);
		SignedTxMsg.EnvelopeBytes(gasPayer).ShouldBe(106 + 32 + 128);

		// A witness-array type takes the count from the caller: each witness is 96 bytes.
		var call = Call();
		var signedCall = CarbonBlob.Serialize(new SignedTxMsg { msg = call, witnesses = new[] { Sign(call, Payer), Sign(call, Owner) } });
		SignedTxMsg.EnvelopeBytes(call, 2).ShouldBe(signedCall.Length);
		(SignedTxMsg.EnvelopeBytes(call, 2) - SignedTxMsg.EnvelopeBytes(call, 1)).ShouldBe(96);
	}

	[Fact]
	public void Refuses_a_witness_count_the_message_does_not_allow()
	{
		Should.Throw<ArgumentException>(() => SignedTxMsg.EnvelopeBytes(Transfer(), 2)).Message.ShouldContain("1 witness(es), not 2");
		Should.Throw<ArgumentException>(() => SignedTxMsg.EnvelopeBytes(GasPayerTransfer(), 1)).Message.ShouldContain("2 witness(es), not 1");
		Should.Throw<ArgumentOutOfRangeException>(() => SignedTxMsg.EnvelopeBytes(Call(), 0));
	}

	[Fact]
	public void Refuses_a_witness_set_that_does_not_match_the_message()
	{
		var gasPayer = GasPayerTransfer();
		// Both signatures present but in the wrong slots: the node would read the owner's signature
		// as the payer's and reject the transaction, so the writer refuses first.
		var swapped = new SignedTxMsg { msg = gasPayer, witnesses = new[] { Sign(gasPayer, Owner), Sign(gasPayer, Payer) } };
		Should.Throw<InvalidOperationException>(() => CarbonBlob.Serialize(swapped)).Message.ShouldContain("Gas witness");

		var oneOnly = new SignedTxMsg { msg = gasPayer, witnesses = new[] { Sign(gasPayer, Payer) } };
		Should.Throw<InvalidOperationException>(() => CarbonBlob.Serialize(oneOnly)).Message.ShouldContain("exactly two");

		// A call may carry any witnesses the caller chooses, except a set without the gas payer.
		var call = Call();
		var withoutPayer = new SignedTxMsg { msg = call, witnesses = new[] { Sign(call, Owner) } };
		Should.Throw<InvalidOperationException>(() => CarbonBlob.Serialize(withoutPayer)).Message.ShouldContain("gas payer must be one of the witnesses");
	}

	[Fact]
	public void Reads_a_gas_payer_envelope_back_with_both_witness_addresses()
	{
		var gasPayer = GasPayerTransfer();
		var bytes = CarbonBlob.Serialize(new SignedTxMsg
		{
			msg = gasPayer,
			witnesses = new[] { Sign(gasPayer, Payer), Sign(gasPayer, Owner) }
		});

		var read = CarbonBlob.New<SignedTxMsg>(bytes);
		read.witnesses.Length.ShouldBe(2);
		read.witnesses[0].address.ShouldBe(PayerPub);
		read.witnesses[1].address.ShouldBe(OwnerPub);
		// The signatures are the ones written, so the envelope round-trips byte for byte.
		CarbonBlob.Serialize(read).ShouldBe(bytes);
	}
}

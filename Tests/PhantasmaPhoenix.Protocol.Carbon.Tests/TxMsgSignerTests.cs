using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

// Multi-witness signing: the envelope order is the node's, not the caller's, for the types whose
// witness set the message fixes. External signers sign the same bytes as in-memory keys. And a
// message without a gas offer is refused before any signature is made.
public class TxMsgSignerTests
{
	private static readonly PhantasmaKeys Payer = PhantasmaKeys.FromWIF("KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d");
	private static readonly PhantasmaKeys Owner = PhantasmaKeys.FromWIF("KwVG94yjfVg1YKFyRxAGtug93wdRbmLnqqrFV6Yd2CiA9KZDAp4H");
	private static readonly Bytes32 PayerPub = new(Payer.PublicKey);
	private static readonly Bytes32 OwnerPub = new(Owner.PublicKey);

	private static TxMsg GasPayerTransfer(Bytes32? from = null) => new()
	{
		type = TxTypes.TransferFungible_GasPayer,
		expiry = 1_787_000_000_000,
		maxGas = 66_600_000,
		maxData = 0,
		gasFrom = PayerPub,
		payload = SmallString.Empty,
		msg = new TxMsgTransferFungible_GasPayer { to = new Bytes32(new byte[32]), from = from ?? OwnerPub, tokenId = 1, amount = 5 }
	};

	private static TxMsg Call() => new()
	{
		type = TxTypes.Call,
		expiry = 1_787_000_000_000,
		maxGas = 100_000_000,
		maxData = 0,
		gasFrom = PayerPub,
		payload = SmallString.Empty,
		msg = new TxMsgCall { moduleId = (uint)ModuleId.Governance, methodId = 1, args = new byte[40] }
	};

	// A signer that stands in for a hardware wallet: asynchronous, and it counts how often it is asked.
	private sealed class CountingSigner : ITxSigner
	{
		private readonly PhantasmaKeys _keys;
		public int Calls { get; private set; }
		public CountingSigner(PhantasmaKeys keys) { _keys = keys; }
		public byte[] PublicKey => _keys.PublicKey;
		public async Task<byte[]> SignAsync(byte[] message, CancellationToken cancellationToken = default)
		{
			Calls++;
			await Task.Yield();
			return Ed25519.Sign(message, _keys.PrivateKey);
		}
	}

	private static void ShouldBeValidlySigned(SignedTxMsg signed)
	{
		var message = CarbonBlob.Serialize(signed.msg);
		foreach (var witness in signed.witnesses)
			Ed25519.Verify(witness.signature.bytes, message, witness.address.bytes).ShouldBeTrue();
	}

	[Fact]
	public void Orders_in_memory_keys_into_the_envelope_order_the_node_expects()
	{
		// The owner is passed first, but the envelope wants the gas payer first.
		var signed = TxMsgSigner.SignWithKeys(GasPayerTransfer(), Owner, Payer);
		signed.witnesses.Select(w => w.address).ShouldBe(new[] { PayerPub, OwnerPub });
		ShouldBeValidlySigned(signed);
		// The serialized form is what the node reads, and its length is the planned envelope size.
		TxMsgSigner.SignAndSerializeWithKeys(GasPayerTransfer(), Owner, Payer).Length.ShouldBe(SignedTxMsg.EnvelopeBytes(GasPayerTransfer()));
	}

	[Fact]
	public async Task Signs_through_asynchronous_signers()
	{
		var payer = new CountingSigner(Payer);
		var owner = new CountingSigner(Owner);
		var signed = await TxMsgSigner.SignWithAsync(GasPayerTransfer(), new ITxSigner[] { owner, payer });
		signed.witnesses.Select(w => w.address).ShouldBe(new[] { PayerPub, OwnerPub });
		ShouldBeValidlySigned(signed);
		payer.Calls.ShouldBe(1);
		owner.Calls.ShouldBe(1);
	}

	[Fact]
	public async Task Asks_a_signer_that_fills_both_witness_slots_to_sign_once()
	{
		// The same account pays the gas and owns the tokens: two slots, one signer, one signature.
		var signer = new CountingSigner(Payer);
		var signed = await TxMsgSigner.SignWithAsync(GasPayerTransfer(from: PayerPub), new ITxSigner[] { signer });
		signed.witnesses.Length.ShouldBe(2);
		signed.witnesses[0].signature.ShouldBe(signed.witnesses[1].signature);
		signer.Calls.ShouldBe(1);
		ShouldBeValidlySigned(signed);
	}

	[Fact]
	public void Refuses_a_signer_set_that_does_not_match_the_witnesses_the_message_needs()
	{
		Should.Throw<ArgumentException>(() => TxMsgSigner.SignWithKeys(GasPayerTransfer(), Payer))
			.Message.ShouldContain("No signer for witness");
		var stray = PhantasmaKeys.Generate();
		Should.Throw<ArgumentException>(() => TxMsgSigner.SignWithKeys(GasPayerTransfer(), Payer, Owner, stray))
			.Message.ShouldContain("is not a witness of this transaction");
	}

	[Fact]
	public void Keeps_the_caller_order_for_witness_array_transactions()
	{
		var signed = TxMsgSigner.SignWithKeys(Call(), Owner, Payer);
		signed.witnesses.Select(w => w.address).ShouldBe(new[] { OwnerPub, PayerPub });
		ShouldBeValidlySigned(signed);
	}

	[Fact]
	public void Refuses_to_sign_a_message_without_a_gas_offer()
	{
		var unplanned = Call();
		unplanned.maxGas = 0;
		Should.Throw<InvalidOperationException>(() => TxMsgSigner.SignWithKeys(unplanned, Payer))
			.Message.ShouldContain("no gas offer");
		Should.Throw<InvalidOperationException>(() => TxMsgSigner.Sign(unplanned, Payer));
	}

	[Fact]
	public async Task Refuses_a_native_transfer_amount_the_chain_cannot_accept()
	{
		// The chain reads a native transfer amount as a signed 64-bit value, so 2^63 fails on chain
		// and the int64 maximum does not. Both transfer types and both signing paths are checked.
		const ulong int64Max = long.MaxValue;
		TxMsg Plain(ulong amount) => new()
		{
			type = TxTypes.TransferFungible,
			expiry = 1_787_000_000_000,
			maxGas = 42_600_000,
			maxData = 0,
			gasFrom = PayerPub,
			payload = SmallString.Empty,
			msg = new TxMsgTransferFungible { to = OwnerPub, tokenId = 1, amount = amount }
		};
		TxMsg WithGasPayer(ulong amount)
		{
			var msg = GasPayerTransfer();
			var body = (TxMsgTransferFungible_GasPayer)msg.msg;
			body.amount = amount;
			msg.msg = body;
			return msg;
		}

		Should.Throw<InvalidOperationException>(() => TxMsgSigner.Sign(Plain(int64Max + 1), Payer))
			.Message.ShouldContain("int64 maximum");
		Should.Throw<InvalidOperationException>(() => TxMsgSigner.SignWithKeys(WithGasPayer(int64Max + 1), Payer, Owner))
			.Message.ShouldContain("int64 maximum");
		(await Should.ThrowAsync<InvalidOperationException>(() => TxMsgSigner.SignWithAsync(Plain(int64Max + 1), new ITxSigner[] { new CountingSigner(Payer) })))
			.Message.ShouldContain("int64 maximum");

		TxMsgSigner.SignWithKeys(Plain(int64Max), Payer).witnesses.Length.ShouldBe(1);
		TxMsgSigner.SignWithKeys(WithGasPayer(int64Max), Payer, Owner).witnesses.Length.ShouldBe(2);
	}

	[Fact]
	public void Still_signs_a_single_witness_message_the_historical_way()
	{
		var msg = new TxMsg
		{
			type = TxTypes.TransferFungible,
			expiry = 1_787_000_000_000,
			maxGas = 42_600_000,
			maxData = 0,
			gasFrom = PayerPub,
			payload = SmallString.Empty,
			msg = new TxMsgTransferFungible { to = OwnerPub, tokenId = 1, amount = 5 }
		};
		var bytes = TxMsgSigner.Sign(msg, Payer);
		bytes.ShouldBe(TxMsgSigner.SignAndSerializeWithKeys(msg, Payer));
		var read = CarbonBlob.New<SignedTxMsg>(bytes);
		read.witnesses.Length.ShouldBe(1);
		read.witnesses[0].address.ShouldBe(PayerPub);
		ShouldBeValidlySigned(read);
	}
}

﻿using System.Numerics;
using PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Hex.HexConvertors.Extensions;
using PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Model;

namespace PhantasmaPhoenix.InteropChains.Legacy.Ethereum.Signer;

public abstract class SignedTransactionBase
{
	public static RLPSigner CreateDefaultRLPSigner(byte[] rawData)
	{
		return new RLPSigner(rawData, NUMBER_ENCODING_ELEMENTS);
	}

	//Number of encoding elements (output for transaction)
	public const int NUMBER_ENCODING_ELEMENTS = 6;
	public static readonly BigInteger DEFAULT_GAS_PRICE = BigInteger.Parse("20000000000");
	public static readonly BigInteger DEFAULT_GAS_LIMIT = BigInteger.Parse("21000");


	protected RLPSigner SimpleRlpSigner { get; set; }

	public byte[] RawHash => SimpleRlpSigner.RawHash;

	/// <summary>
	///     The counter used to make sure each transaction can only be processed once, you may need to regenerate the
	///     transaction if is too low or too high, simples way is to get the number of transacations
	/// </summary>
	public byte[] Nonce => SimpleRlpSigner.Data[0] ?? DefaultValues.ZERO_BYTE_ARRAY;

	public byte[] Value => SimpleRlpSigner.Data[4] ?? DefaultValues.ZERO_BYTE_ARRAY;

	public byte[] ReceiveAddress => SimpleRlpSigner.Data[3];

	public byte[] GasPrice => SimpleRlpSigner.Data[1] ?? DefaultValues.ZERO_BYTE_ARRAY;

	public byte[] GasLimit => SimpleRlpSigner.Data[2];

	public byte[] Data => SimpleRlpSigner.Data[5];

	public EthECDSASignature Signature => SimpleRlpSigner.Signature;

	public abstract EthECKey Key { get; }


	public byte[] GetRLPEncoded()
	{
		return SimpleRlpSigner.GetRLPEncoded();
	}

	public byte[] GetRLPEncodedRaw()
	{
		return SimpleRlpSigner.GetRLPEncodedRaw();
	}

	public virtual void Sign(EthECKey key)
	{
		SimpleRlpSigner.Sign(key);
	}

	public void SetSignature(EthECDSASignature signature)
	{
		SimpleRlpSigner.SetSignature(signature);
	}

	protected static string ToHex(byte[] x)
	{
		if (x == null) return "0x";
		return x.ToHex();
	}
#if !DOTNET35
	public abstract Task SignExternallyAsync(IEthExternalSigner externalSigner);
#endif
}

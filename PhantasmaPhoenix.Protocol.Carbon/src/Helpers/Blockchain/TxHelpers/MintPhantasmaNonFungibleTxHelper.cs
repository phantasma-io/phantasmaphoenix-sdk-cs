using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

/// <summary>A deterministic Phantasma NFT mint. It is one call with one recipient and one entry per
/// minted instance.</summary>
public sealed class PhantasmaNftMintParams
{
	public ulong TokenId { get; set; }

	/// <summary>The account that pays the gas and signs the mint.</summary>
	public Bytes32 Sender { get; set; }

	/// <summary>The account that receives every instance this call mints.</summary>
	public Bytes32 To { get; set; }

	/// <summary>
	/// One entry per instance. Each entry names the Phantasma series the instance is minted into and
	/// carries the public ROM. Instances of several series may share one call. A single mint passes one
	/// entry.
	/// </summary>
	public PhantasmaNftMintInfo[] Tokens { get; set; } = Array.Empty<PhantasmaNftMintInfo>();
}

public static class MintPhantasmaNonFungibleTxHelper
{
	/// <summary>Builds the Token.MintPhantasmaNonFungible call. Fees are planned from the message
	/// afterwards.</summary>
	public static TxMsg BuildTx(PhantasmaNftMintParams p, TxLimits? limits = null)
	{
		if (p == null) throw new ArgumentNullException(nameof(p));
		if (p.Tokens == null || p.Tokens.Length == 0)
			throw new ArgumentException("Tokens must not be empty", nameof(p));

		// This helper only packages the Token.Call ABI surface.
		return TxLimits.Apply(new TxMsg
		{
			type = TxTypes.Call,
			gasFrom = p.Sender,
			payload = SmallString.Empty,
			msg = new TxMsgCall
			{
				moduleId = (uint)ModuleId.Token,
				methodId = (uint)TokenContract_Methods.MintPhantasmaNonFungible,
				args = CarbonBlob.Serialize(new MintPhantasmaNonFungibleArgs
				{
					tokenId = p.TokenId,
					address = p.To,
					tokens = (PhantasmaNftMintInfo[])p.Tokens.Clone()
				})
			}
		}, limits);
	}

	/// <summary>
	/// Builds the call, plans it against <paramref name="config"/>, signs it with in-memory keys and
	/// returns the envelope bytes. The signer must be <c>p.Sender</c>.
	/// </summary>
	public static byte[] BuildTxAndSign(PhantasmaNftMintParams p, PhantasmaKeys signer, in GasConfig config, PlanAndSignOptions? options = null)
	{
		var tx = BuildTx(p, options?.Limits);
		return PlanAndSign.WithKeys(tx, new IKeyPair[] { signer }, config, options);
	}

	public static string BuildTxAndSignHex(PhantasmaNftMintParams p, PhantasmaKeys signer, in GasConfig config, PlanAndSignOptions? options = null)
	{
		return BuildTxAndSign(p, signer, config, options).ToHex();
	}

	public static PhantasmaNftMintResult[] ParseResult(string resultHex)
	{
		var bytes = Base16.Decode(resultHex) ?? Array.Empty<byte>();
		using var s = new MemoryStream(bytes);
		using var r = new BinaryReader(s);
		r.ReadArray(out PhantasmaNftMintResult[] result);
		return result;
	}
}

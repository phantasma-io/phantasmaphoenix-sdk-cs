using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

public static class CreateTokenTxHelper
{
	/// <summary>Builds the Token.CreateToken call. Fees are planned from the message afterwards.</summary>
	public static TxMsg BuildTx(TokenInfo tokenInfo, Bytes32 creatorPublicKey, TxLimits? limits = null)
	{
		return TxLimits.Apply(new TxMsg
		{
			type = TxTypes.Call,
			gasFrom = creatorPublicKey,
			payload = SmallString.Empty,
			msg = new TxMsgCall
			{
				moduleId = (uint)ModuleId.Token,
				methodId = (uint)TokenContract_Methods.CreateToken,
				args = CarbonBlob.Serialize(tokenInfo)
			}
		}, limits);
	}

	/// <summary>Builds the call, plans it against <paramref name="config"/>, signs it with in-memory
	/// keys and returns the envelope bytes.</summary>
	public static byte[] BuildTxAndSign(TokenInfo tokenInfo, PhantasmaKeys signer, in GasConfig config, PlanAndSignOptions? options = null)
	{
		var tx = BuildTx(tokenInfo, new Bytes32(signer.PublicKey), options?.Limits);
		return PlanAndSign.WithKeys(tx, new IKeyPair[] { signer }, config, options);
	}

	public static string BuildTxAndSignHex(TokenInfo tokenInfo, PhantasmaKeys signer, in GasConfig config, PlanAndSignOptions? options = null)
	{
		return BuildTxAndSign(tokenInfo, signer, config, options).ToHex();
	}

	/// <summary>
	/// The token id the creation returned. Token.CreateToken answers with a u64, and a token id is a
	/// ulong everywhere else in the SDK. Reading only its low half would return a wrong id and would
	/// not fail.
	/// </summary>
	public static ulong ParseResult(string resultHex)
	{
		ulong carbonTokenId;
		using (var s = new MemoryStream(Base16.Decode(resultHex) ?? Array.Empty<byte>()))
		using (var r = new BinaryReader(s))
		{
			BinaryStreamExt.Read8(r, out carbonTokenId);
		}

		return carbonTokenId;
	}
}

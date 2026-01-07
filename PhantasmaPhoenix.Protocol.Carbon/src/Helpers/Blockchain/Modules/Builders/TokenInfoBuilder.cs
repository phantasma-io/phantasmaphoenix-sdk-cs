using System.Numerics;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;

public static class TokenInfoBuilder
{
	private const int MaxSymbolLength = 255;

	public static TokenInfo Build(
		string symbol,
		IntX maxSupply,
		bool isNFT,
		uint decimals,
		Bytes32 creatorPublicKey,
		byte[]? metadata = null,
		byte[]? tokenSchemas = null)
	{
		var (ok, error) = CheckIsValidSymbol(symbol);
		if (!ok)
		{
			throw new ArgumentException(error ?? "Symbol validation error", nameof(symbol));
		}

		var tokenInfo = new TokenInfo
		{
			maxSupply = maxSupply,
			flags = TokenFlags.None,
			decimals = decimals,
			owner = creatorPublicKey,
			symbol = new SmallString(symbol)
		};

		if (metadata == null)
		{
			throw new ArgumentException("metadata is required", nameof(metadata));
		}

		if (isNFT)
		{
			// NFTs must fit inside Int64 to match Carbon contract constraints.
			if (!IsInt64Safe(maxSupply))
			{
				throw new ArgumentException("NFT maximum supply must fit into Int64", nameof(maxSupply));
			}

			tokenInfo.flags = TokenFlags.NonFungible;

			if (tokenSchemas == null)
			{
				throw new ArgumentException("tokenSchemas is required for NFTs", nameof(tokenSchemas));
			}

			tokenInfo.metadata = metadata;
			tokenInfo.tokenSchemas = tokenSchemas;
		}
		else
		{
			if (!IsInt64Safe(maxSupply))
			{
				tokenInfo.flags = TokenFlags.BigFungible;
			}

			tokenInfo.metadata = metadata;
		}

		return tokenInfo;
	}

	private static bool IsInt64Safe(IntX value)
	{
		var big = (BigInteger)value;
		return big >= long.MinValue && big <= long.MaxValue;
	}

	// Mirrors carbon::CheckIsValidSymbol from contracts/token.cpp.
	private static (bool ok, string? error) CheckIsValidSymbol(string symbol)
	{
		if (string.IsNullOrEmpty(symbol))
		{
			return (false, "Symbol validation error: Empty string is invalid");
		}

		if (symbol.Length > MaxSymbolLength)
		{
			return (false, "Symbol validation error: Too long");
		}

		for (int i = 0; i < symbol.Length; i++)
		{
			var code = symbol[i];
			var isUppercaseAsciiLetter = code >= 'A' && code <= 'Z';
			if (!isUppercaseAsciiLetter)
			{
				return (false, "Symbol validation error: Anything outside A-Z is forbidden (digits, accents, etc.)");
			}
		}

		return (true, null);
	}
}

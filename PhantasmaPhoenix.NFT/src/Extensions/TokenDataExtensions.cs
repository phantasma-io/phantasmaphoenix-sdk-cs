using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.RPC.Models;

namespace PhantasmaPhoenix.NFT.Extensions;

public static class TokenDataExtensions
{
	public static IRom ParseRom(this TokenDataResult tokenData, string symbol)
	{
		switch (symbol)
		{
			case "CROWN":
				return new CrownRom(Base16.Decode(tokenData.Rom), tokenData.Id);
			default:
				return new VMDictionaryRom(Base16.Decode(tokenData.Rom));
		}
	}

	public static string? GetPropertyValue(this TokenDataResult tokenData, string key)
	{
		if (tokenData.Properties != null)
		{
			return tokenData.Properties.Where(x => x.Key.ToUpperInvariant() == key.ToUpperInvariant()).Select(x => x.Value).FirstOrDefault();
		}

		return null;
	}
}

using System.Globalization;
using System.Numerics;

namespace PhantasmaPhoenix.Core;

public static class UnitConversion
{
	private static readonly string s_NumberDecimalSeparator = ".";
	private static readonly NumberFormatInfo s_NumberFormatInfo = new() { NumberDecimalSeparator = s_NumberDecimalSeparator };

	private static BigInteger GetMultiplier(int units)
	{
#if NET7_0_OR_GREATER
            return BigInteger.Pow(10, units);
#else
		BigInteger unitMultiplier = 1;
		while (units > 0)
		{
			unitMultiplier *= 10;
			units--;
		}

		return unitMultiplier;
#endif
	}

	public static string ToDecimalString(string amount, int tokenDecimals)
	{
		if (string.IsNullOrEmpty(amount) || amount == "0" || tokenDecimals == 0)
			return "0";

		if (amount.Length <= tokenDecimals)
		{
			var fraction = amount.PadLeft(tokenDecimals, '0').TrimEnd('0');
			return "0" + s_NumberDecimalSeparator + (fraction.Length > 0 ? fraction : "0");
		}

		var whole = amount.Substring(0, amount.Length - tokenDecimals);
		var fractionPart = amount.Substring(amount.Length - tokenDecimals).TrimEnd('0');
		return whole + (fractionPart.Length > 0 ? s_NumberDecimalSeparator + fractionPart : "");
	}

	public static decimal ToDecimal(string amount, int tokenDecimals)
	{
		return decimal.Parse(ToDecimalString(amount, tokenDecimals), s_NumberFormatInfo);
	}

	public static decimal ToDecimal(BigInteger value, int tokenDecimals)
	{
		return ToDecimal(value.ToString(), tokenDecimals);
	}

	public static BigInteger ToBigInteger(decimal n, int units)
	{
		var multiplier = GetMultiplier(units);
		var A = new BigInteger((long)n);
		var B = new BigInteger((long)multiplier);

		var fracPart = n - Math.Truncate(n);
		BigInteger C = 0;

		if (fracPart > 0)
		{
			var l = fracPart * (long)multiplier;
			C = new BigInteger((long)l);
		}

		return A * B + C;
	}

	public static BigInteger ConvertDecimals(BigInteger value, int decimalFrom, int decimalTo)
	{
		if (decimalFrom == decimalTo)
		{
			return value;
		}

		//doing "value * BigInteger.Pow(10, decimalTo - decimalFrom)" would not work for negative exponents as it would always be 0;
		//separating the calculations in two steps leads to only returning 0 when the final value would be < 1
		var fromFactor = GetMultiplier(decimalFrom);
		var toFactor = GetMultiplier(decimalTo);

		return value * toFactor / fromFactor;
	}

	public static BigInteger GetUnitValue(int decimals)
	{
		return ToBigInteger(1, decimals);
	}
}

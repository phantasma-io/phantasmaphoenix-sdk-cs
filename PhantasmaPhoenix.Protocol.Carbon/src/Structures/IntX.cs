using System;
using System.Numerics;
using PhantasmaPhoenix.Core;

namespace PhantasmaPhoenix.Protocol.Carbon;

public struct IntX : ICarbonBlob
{
	private BigInteger big = BigInteger.Zero;
	private Int64 small = 0;
	private bool isBig;

	public IntX(BigInteger v)
	{
		big = v;
		isBig = true;
	}
	public IntX(Int64 v)
	{
		small = v;
		isBig = false;
	}

	public override string ToString()
	{
		return isBig ? big.ToString() : small.ToString();
	}
	public bool IsZero
	{
		get
		{
			if (isBig)
				return big.IsZero;
			return small == 0;
		}
	}
	public static explicit operator BigInteger(IntX self)
	{
		if (self.isBig)
			return self.big;
		return new BigInteger(self.small);
	}
	public static explicit operator Int64(IntX self)
	{
		if (self.isBig)
		{
			try
			{
				return (long)self.big;
			}
			catch
			{
				return self.big > 0 ? Int64.MaxValue : Int64.MinValue;
			}
		}
		return self.small;
	}

	public static IntX operator -(IntX a)
	{
		if (!a.isBig)
			return new IntX(-a.small);
		return new IntX(-a.big);
	}
	public static IntX operator -(IntX a, IntX b)
	{
		if (!a.isBig && !b.isBig)
			return new IntX(a.small - b.small);//todo handle overflox
		BigInteger biga = a.isBig ? a.big : new BigInteger(a.small);
		BigInteger bigb = b.isBig ? b.big : new BigInteger(b.small);
		return new IntX(biga - bigb);
	}


	public void Write(BinaryWriter w)
	{
		Int64 value;
		if (isBig)
		{
			try
			{
				value = (long)big;
			}
			catch
			{
				w.WriteBigInt(big);
				return;
			}
		}
		else
			value = small;

		int header = value < 0 ? 0x88 : 0x08;
		w.Write1((byte)header);
		w.Write8(value);
	}

	public void Read(BinaryReader r)
	{
		byte header = r.ReadByte();
		Throw.If((header & 0x3F) < 8, "invalid intx packing");
		if ((header & 0x3F) == 8) // it's an 8 byte value
		{
			r.Read8(out small);
			bool headerIsNegative = (header & 0x80) != 0;
			bool valueIsNegative = small < 0;
			if (headerIsNegative == valueIsNegative)
			{
				isBig = false;
			}
			else
			{
				byte[] rawBytes = BitConverter.GetBytes(small);
				byte fill = headerIsNegative ? (byte)0xFF : (byte)0x00;
				byte[] bigBytes = new byte[32];
				Array.Copy(rawBytes, bigBytes, rawBytes.Length);
				for (int i = rawBytes.Length; i < bigBytes.Length; i++)
				{
					bigBytes[i] = fill;
				}

				isBig = true;
				big = new BigInteger(bigBytes);
			}
		}
		else
		{
			isBig = true;
			r.ReadBigInt(out big, header);
		}
	}
}

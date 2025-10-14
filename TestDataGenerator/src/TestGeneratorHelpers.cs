using System.Globalization;
using System.Numerics;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Protocol.Carbon;

public static class TestGeneratorHelpers
{
	static StreamWriter? file = null;
	static TextWriter Output = Console.Out;

	public static void Init(string path)
	{
		var dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		file = new StreamWriter(path, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		Output = file;
	}
	public static void Uninit()
	{
		file?.Dispose();
	}

	public static void Emit(string kind, string value, string serialized)
    {
        Output.WriteLine($"{kind}\t{value}\t{serialized}");
    }

	public static void Emit(string kind, string value, Action<BinaryWriter> write)
	{
		using var ms = new MemoryStream();
		using var w = new BinaryWriter(ms);
		write(w);
		var hex = ms.ToArray().ToHex();
		Emit(kind, value, hex);
	}

	public static void Big(string kind, string value, BigInteger v)
	{
		using var ms = new MemoryStream();
		using var w = new BinaryWriter(ms);
		w.WriteBigInt(v);
		var hex = ms.ToArray().ToHex();

		// also compute back via reader
		var back = ReadBackBigInt(ms.ToArray());
		Output.WriteLine($"{kind}\t{value}\t{hex}\t{v.ToString(CultureInfo.InvariantCulture)}\t{back.ToString(CultureInfo.InvariantCulture)}");
	}

	public static void IntXEmit(string kind, string value, IntX v)
	{
		var serialized = CarbonBlob.Serialize(v);
		var hex = serialized.ToHex();

		// also compute back via reader
		var back = CarbonBlob.New<IntX>(serialized);
        Output.WriteLine($"{kind}\t{value}\t{hex}\t{v.ToString()}\t{back.ToString()}");
    }

	public static BigInteger ReadBackBigInt(byte[] bytes)
	{
		using var ms = new MemoryStream(bytes);
		using var r = new BinaryReader(ms);
		return r.ReadBigInt();
	}
}

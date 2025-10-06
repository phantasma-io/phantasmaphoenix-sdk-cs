// Emits TSV lines that encode test data with Carbon serialization
// Columns:
//  - For non-BigInt:  KIND \t VALUE \t HEX
//  - For BigInt:      KIND \t VALUE \t HEX \t DEC_ORIG \t DEC_BACK

using System.Globalization;
using System.Numerics;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;

class Program
{
	static TextWriter Output = Console.Out;

	static void Main(string[] args)
	{
		StreamWriter? file = null;
		if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
		{
			var path = args[0];
			var dir = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
			file = new StreamWriter(path, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			Output = file;
		}

		// --- Primitives --------------------------------------------------
		var u8 = byte.MaxValue;
		Emit("U8", u8.ToString(CultureInfo.InvariantCulture), w => w.Write1(u8));
		u8 = 0;
		Emit("U8", u8.ToString(CultureInfo.InvariantCulture), w => w.Write1(u8));
		u8 = 0x7F;
		Emit("U8", u8.ToString(CultureInfo.InvariantCulture), w => w.Write1(u8));

		var i16 = short.MaxValue;
		Emit("I16", i16.ToString(CultureInfo.InvariantCulture), w => w.Write2(i16));
		i16 = 0;
		Emit("I16", i16.ToString(CultureInfo.InvariantCulture), w => w.Write2(i16));
		i16 = -12345;
		Emit("I16", i16.ToString(CultureInfo.InvariantCulture), w => w.Write2(i16));

		var i32 = int.MaxValue;
		Emit("I32", i32.ToString(CultureInfo.InvariantCulture), w => w.Write4(i32));
		i32 = 0;
		Emit("I32", i32.ToString(CultureInfo.InvariantCulture), w => w.Write4(i32));
		i32 = -12345;
		Emit("I32", i32.ToString(CultureInfo.InvariantCulture), w => w.Write4(i32));

		var u32 = uint.MaxValue;
		Emit("U32", u32.ToString(CultureInfo.InvariantCulture), w => w.Write4(u32));
		u32 = 0;
		Emit("U32", u32.ToString(CultureInfo.InvariantCulture), w => w.Write4(u32));

		var i64 = long.MaxValue;
		Emit("I64", i64.ToString(CultureInfo.InvariantCulture), w => w.Write8(i64));
		i64 = 0;
		Emit("I64", i64.ToString(CultureInfo.InvariantCulture), w => w.Write8(i64));
		i64 = -1234567890123456789L;
		Emit("I64", i64.ToString(CultureInfo.InvariantCulture), w => w.Write8(i64));

		var u64 = ulong.MaxValue;
		Emit("U64", u64.ToString(CultureInfo.InvariantCulture), w => w.Write8(u64));
		u64 = 0;
		Emit("U64", u64.ToString(CultureInfo.InvariantCulture), w => w.Write8(u64));
		u64 = 1234567890123456789L;
		Emit("U64", u64.ToString(CultureInfo.InvariantCulture), w => w.Write8(u64));

		// --- Fixed byte arrays ------------------------------------------
		var fix16 = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
		Emit("FIX16", fix16.ToHex(), w =>	w.Write16(fix16));
		fix16 = Enumerable.Repeat((byte)0xFF, 16).ToArray();
		Emit("FIX16", fix16.ToHex(), w =>	w.Write16(fix16));

		var fix32 = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
		Emit("FIX32", fix32.ToHex(), w => w.Write32(fix32));
		fix32 = Enumerable.Repeat((byte)0xFF, 32).ToArray();
		Emit("FIX32", fix32.ToHex(), w => w.Write32(fix32));

		var fix64 = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
		Emit("FIX64", fix64.ToHex(), w => w.Write64(fix64));
		fix64 = Enumerable.Repeat((byte)0xFF, 64).ToArray();
		Emit("FIX64", fix64.ToHex(), w => w.Write64(fix64));

		// --- Zero-terminated UTF-8 strings ------------------------------
		var ztStr = "hello";
		Emit("SZ", ztStr, w => w.WriteSz(ztStr));
		ztStr = "привет";
		Emit("SZ", ztStr, w => w.WriteSz(ztStr));
		ztStr = "你好";
		Emit("SZ", ztStr, w => w.WriteSz(ztStr));
		ztStr = "Jamás te rindas, pase lo que pase";
		Emit("SZ", ztStr, w => w.WriteSz(ztStr));

		var sarr = new[] { "A", "Б", "你好" };
		Emit("ARRSZ", string.Join(",", sarr), w => w.WriteArraySz(sarr));

		// --- Variable-length arrays -------------------------------------
		var arr8 = new sbyte[] { 1, 2, 3 };
		Emit("ARR8", string.Join(",", arr8.Select(x  => x.ToString(CultureInfo.InvariantCulture))), w => w.WriteArray8(arr8));

		var arr16 = new short[] { 300, -300 };
		Emit("ARR16", string.Join(",", arr16.Select(x  => x.ToString(CultureInfo.InvariantCulture))), w => w.WriteArray16(arr16));

		var arr32 = new int[] { 123456, -654321 };
		Emit("ARR32", string.Join(",", arr32.Select(x  => x.ToString(CultureInfo.InvariantCulture))), w => w.WriteArray32(arr32));

		var arr64 = new long[] { long.MaxValue, -1, long.MaxValue };
		Emit("ARR64", string.Join(",", arr64.Select(x  => x.ToString(CultureInfo.InvariantCulture))), w => w.WriteArray64(arr64));

		var arru64 = new ulong[] { ulong.MaxValue, 1, ulong.MaxValue };
		Emit("ARRU64", string.Join(",", arru64.Select(x  => x.ToString(CultureInfo.InvariantCulture))), w => w.WriteArray64(arru64));

		var a_1d = new byte[] { 0x00, 0x01, 0xFF, 0x80 };
		Emit("ARRBYTES-1D", a_1d.ToHex(), w => w.WriteArray(a_1d));

		var a_2d = new byte[][] { [1, 2], [3, 4, 5] };
		Emit("ARRBYTES-2D", "[[01,02],[03,04,05]]", w => w.WriteArray(a_2d));

		// --- BigInt compact format (<=32 bytes, header+payload) ---------
		BigInteger bi = 0;
		Big("BI", bi.ToString(), bi);
		bi = 1;
		Big("BI", bi.ToString(), bi);
		bi = 255;
		Big("BI", bi.ToString(), bi);
		bi = 256;
		Big("BI", bi.ToString(), bi);
		bi = -1;
		Big("BI", bi.ToString(), bi);
		bi = -255;
		Big("BI", bi.ToString(), bi);

		bi = (BigInteger.One << 63) - 1;
		Big("BI", bi.ToString(), bi);

		bi = -(BigInteger.One << 63);
		Big("BI", bi.ToString(), bi);

		bi = (BigInteger.One << 255) - 1;
		Big("BI", bi.ToString(), bi);

		bi = BigInteger.One << 255;
		Big("BI", bi.ToString(), bi);

		bi = (BigInteger.One << 256) - 1; // will clamp to 32 bytes and read as -1
		Big("BI", bi.ToString(), bi);

		// --- Array of BigInt --------------------------------------------
		{
			var arr = new BigInteger[] { 0, 1, -1, 255, -255, (BigInteger.One << 200) + 5, -((BigInteger.One << 199) + 7) };
			Emit("ARRBI", string.Join(",", arr.Select(x => x.ToString(CultureInfo.InvariantCulture))),
			w =>
			{
				w.WriteArrayBigInt(arr);
			});
		}

		// --- Carbon tx serialization ------------------------------------
		var tx = new TxMsg
		{
			type = TxTypes.TransferFungible,
			expiry = 1759711416000,
			maxGas = 10000000,
			maxData = 1000,
			gasFrom = new Bytes32(),
			payload = new SmallString("test-payload"),
			msg = new TxMsgTransferFungible
			{
				to = new Bytes32(),
				tokenId = 1,
				amount = 100000000
			}
		};

		var txres = CarbonBlob.Serialize(tx).ToHex();
		Emit("TX1", txres, txres);

		var txSender = PhantasmaKeys.FromWIF("KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d");
		var txReceiver = PhantasmaKeys.FromWIF("KwVG94yjfVg1YKFyRxAGtug93wdRbmLnqqrFV6Yd2CiA9KZDAp4H");

		tx = new TxMsg
		{
			type = TxTypes.TransferFungible,
			expiry = 1759711416000,
			maxGas = 10000000,
			maxData = 1000,
			gasFrom = new Bytes32(txSender.PublicKey),
			payload = new SmallString("test-payload"),
			msg = new TxMsgTransferFungible
			{
				to = new Bytes32(txReceiver.PublicKey),
				tokenId = 1,
				amount = 100000000
			}
		};

		var signedTxMsg = new SignedTxMsg
		{
			msg = tx,
			witnesses = new Witness[] {new Witness
			{
				address = new Bytes32(txSender.PublicKey),
				signature = new Bytes64(Ed25519.Sign(CarbonBlob.Serialize(tx), txSender.PrivateKey))
			}}
		};

		txres = CarbonBlob.Serialize(signedTxMsg).ToHex();
		Emit("TX2", txres, txres);

		file?.Dispose();
	}

	static void Emit(string kind, string value, string serialized)
    {
        Output.WriteLine($"{kind}\t{value}\t{serialized}");
    }

	static void Emit(string kind, string value, Action<BinaryWriter> write)
	{
		using var ms = new MemoryStream();
		using var w = new BinaryWriter(ms);
		write(w);
		var hex = ms.ToArray().ToHex();
		Emit(kind, value, hex);
	}

    static void Big(string kind, string value, BigInteger v)
    {
        using var ms = new MemoryStream();
        using var w  = new BinaryWriter(ms);
        w.WriteBigInt(v);
        var hex = ms.ToArray().ToHex();

        // also compute back via reader
        var back = ReadBackBigInt(ms.ToArray());
        Output.WriteLine($"{kind}\t{value}\t{hex}\t{v.ToString(CultureInfo.InvariantCulture)}\t{back.ToString(CultureInfo.InvariantCulture)}");
    }

    static BigInteger ReadBackBigInt(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var r  = new BinaryReader(ms);
        return r.ReadBigInt();
    }
}

using System.Numerics;
using PhantasmaPhoenix.Core;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

using static VmGlobal;

public struct VmDynamicVariable : ICarbonBlob
{
	public VmType type;
	public object? data;

	//VmDynamicVariable() = default;
	//VmDynamicVariable(const VmDynamicVariable&);
	//VmDynamicVariable(ByteView v) : type(VmType.Bytes), arrayLength(1) { data.bytes = v; }
	//VmDynamicVariable(const VmDynamicStruct& v) : type(VmType.Struct), arrayLength(1) { data.structure = v; }
	//VmDynamicVariable(uint8_t v) : type(VmType.Int8), arrayLength(1) { data.int8 = v; }
	//VmDynamicVariable( int8_t v) : type(VmType.Int8), arrayLength(1) { data.int8 = v; }
	//VmDynamicVariable(uint16_t v) : type(VmType.Int16), arrayLength(1) { data.int16= v; }
	//VmDynamicVariable( int16_t v) : type(VmType.Int16), arrayLength(1) { data.int16= v; }
	//VmDynamicVariable(UInt32 v) : type(VmType.Int32), arrayLength(1) { data.int32 = v; }
	//VmDynamicVariable( int32_t v) : type(VmType.Int32), arrayLength(1) { data.int32 = v; }
	//VmDynamicVariable(UInt64 v) : type(VmType.Int64), arrayLength(1) { data.int64 = v; }
	//VmDynamicVariable( int64_t v) : type(VmType.Int64), arrayLength(1) { data.int64 = v; }
	//VmDynamicVariable(const  int256& v) : type(VmType.Int256), arrayLength(1) { data.int256 = v.Unsigned(); }
	//VmDynamicVariable(const uint256& v) : type(VmType.Int256), arrayLength(1) { data.int256 = v; }
	//VmDynamicVariable(const Bytes16& v) : type(VmType.Bytes16), arrayLength(1) { data.bytes16 = v; }
	//VmDynamicVariable(const Bytes32& v) : type(VmType.Bytes32), arrayLength(1) { data.bytes32 = v; }
	//VmDynamicVariable(const Bytes64& v) : type(VmType.Bytes64), arrayLength(1) { data.bytes64 = v; }
	//VmDynamicVariable(const char* v) : type(VmType.String), arrayLength(1) { data.string = v; }
	public void Write(BinaryWriter w)
	{
		w.Write1(type);
		Write(type, this, null, w);
	}
	public bool Write(VmVariableSchema schema, BinaryWriter w)
	{
		return Write(schema.type, this, schema.structure, w);
	}

	public void Read(BinaryReader r)
	{
		r.Read1(out type);
		Read(type, out this, null, r);
	}
	public void Read(VmVariableSchema schema, BinaryReader r)
	{
		Read(schema.type, out this, null, r);
	}

	public VmDynamicVariable() { type = VmType.Dynamic; data = null; }
	public VmDynamicVariable(byte[] b) { type = VmType.Bytes; data = b; }
	public VmDynamicVariable(byte i) { type = VmType.Int8; data = i; }
	public VmDynamicVariable(sbyte i) { type = VmType.Int8; data = (byte)i; }
	public VmDynamicVariable(UInt16 i) { type = VmType.Int16; data = (Int16)i; }
	public VmDynamicVariable(Int16 i) { type = VmType.Int16; data = i; }
	public VmDynamicVariable(UInt32 i) { type = VmType.Int32; data = (Int32)i; }
	public VmDynamicVariable(Int32 i) { type = VmType.Int32; data = i; }
	public VmDynamicVariable(UInt64 i) { type = VmType.Int64; data = (Int64)i; }
	public VmDynamicVariable(Int64 i) { type = VmType.Int64; data = i; }
	public VmDynamicVariable(BigInteger i) { type = VmType.Int256; data = i; }
	public VmDynamicVariable(Bytes16 b) { type = VmType.Bytes16; data = b; }
	public VmDynamicVariable(Bytes32 b) { type = VmType.Bytes32; data = b; }
	public VmDynamicVariable(Bytes64 b) { type = VmType.Bytes64; data = b; }
	public VmDynamicVariable(string s) { type = VmType.String; data = s; }
	public VmDynamicVariable(VmStructArray s) { type = VmType.Array | VmType.Struct; data = s; }
	public VmDynamicVariable(VmType t)
	{
		type = t;
		switch (t)
		{
			case VmType.Dynamic: data = null; break;
			case VmType.Bytes: data = new byte[0]; break;
			case VmType.Struct: data = new VmDynamicStruct(); break;
			case VmType.Int8: data = (byte)0; break;
			case VmType.Int16: data = (Int16)0; break;
			case VmType.Int32: data = (Int32)0; break;
			case VmType.Int64: data = (Int64)0; break;
			case VmType.Int256: data = new BigInteger(); break;
			case VmType.Bytes16: data = new Bytes16(); break;
			case VmType.Bytes32: data = new Bytes32(); break;
			case VmType.Bytes64: data = new Bytes64(); break;
			case VmType.String: data = ""; break;
			case VmType.Array | VmType.Dynamic: data = new VmDynamicVariable[0]; break;
			case VmType.Array | VmType.Bytes: data = new byte[0][]; break;
			case VmType.Array | VmType.Struct: data = new VmStructArray(); break;
			case VmType.Array | VmType.Int8: data = new byte[0]; break;
			case VmType.Array | VmType.Int16: data = new Int16[0]; break;
			case VmType.Array | VmType.Int32: data = new Int32[0]; break;
			case VmType.Array | VmType.Int64: data = new Int64[0]; break;
			case VmType.Array | VmType.Int256: data = new BigInteger[0]; break;
			case VmType.Array | VmType.Bytes16: data = new Bytes16[0]; break;
			case VmType.Array | VmType.Bytes32: data = new Bytes32[0]; break;
			case VmType.Array | VmType.Bytes64: data = new Bytes64[0]; break;
			case VmType.Array | VmType.String: data = new string[0]; break;
			default:
				data = null;
				throw new InvalidDataException("Unknown VmDynamicVariable type");
		}
	}

	public byte[] GetBytes(string e = "not Bytes") { VmExpect(type == VmType.Bytes, e); VmAssert(data != null, "bad variable"); return (byte[])data!; }
	public string GetString(string e = "not String") { VmExpect(type == VmType.String, e); VmAssert(data != null, "bad variable"); return (string)data!; }
	public byte GetUInt8(string e = "not Int8") { VmExpect(type == VmType.Int8, e); VmAssert(data != null, "bad variable"); return (byte)data!; }
	public sbyte GetInt8(string e = "not Int8") { VmExpect(type == VmType.Int8, e); VmAssert(data != null, "bad variable"); return (sbyte)(byte)data!; }
	public UInt16 GetUInt16(string e = "not Int16") { VmExpect(type == VmType.Int16, e); VmAssert(data != null, "bad variable"); return (UInt16)(Int16)data!; }
	public Int16 GetInt16(string e = "not Int16") { VmExpect(type == VmType.Int16, e); VmAssert(data != null, "bad variable"); return (Int16)data!; }
	public UInt32 GetUInt32(string e = "not Int32") { VmExpect(type == VmType.Int32, e); VmAssert(data != null, "bad variable"); return (UInt32)(Int32)data!; }
	public Int32 GetInt32(string e = "not Int32") { VmExpect(type == VmType.Int32, e); VmAssert(data != null, "bad variable"); return (Int32)data!; }
	public UInt64 GetUInt64(string e = "not Int64") { VmExpect(type == VmType.Int64, e); VmAssert(data != null, "bad variable"); return (UInt64)(Int64)data!; }
	public Int64 GetInt64(string e = "not Int64") { VmExpect(type == VmType.Int64, e); VmAssert(data != null, "bad variable"); return (Int64)data!; }
	public BigInteger GetInt256(string e = "not Int256") { VmExpect(type == VmType.Int256, e); VmAssert(data != null, "bad variable"); return (BigInteger)data!; }
	public BigInteger GetUInt256(string e = "not Int256")
	{
		BigInteger i = GetInt256(e);
		if (i.Sign < 0)
		{
			var bytes = i.ToByteArray();
			if (bytes.Length < 32)
			{
				int oldSize = bytes.Length;
				Array.Resize(ref bytes, 32);
				for (int idx = oldSize; idx < 32; ++idx)
					bytes[idx] = (byte)0xFF;
			}


#if NET6_0_OR_GREATER
			i = new BigInteger(bytes, true, false);
#else
			// BigInteger(byte[]) expects little-endian two's complement (signed)
			// For unsigned input, append a zero high byte to keep it positive when MSB is set
			if (bytes.Length == 0)
			{
				return BigInteger.Zero;
			}

			var copy = new byte[bytes.Length + 1];
			Buffer.BlockCopy(bytes, 0, copy, 0, bytes.Length);
			return new BigInteger(copy);
#endif
		}
		return i;
	}

	public static void Read(VmType type, out VmDynamicVariable v, VmStructSchema? schema, BinaryReader r)
	{
		switch (type)
		{
			case VmType.Dynamic:
				v = new VmDynamicVariable();
				v.Read(r);
				break;
			case VmType.Bytes:
				byte[] bytes;
				r.ReadArray(out bytes);
				v = new(bytes);
				break;
			case VmType.Struct:
				VmDynamicStruct str = new();
				if (schema != null)
					str.Read(schema.Value, r);
				else
					str.Read(r);
				v = new VmDynamicVariable { type = type, data = str };
				break;
			case VmType.Int8: v = new(r.Read1()); break;
			case VmType.Int16: v = new(r.Read2()); break;
			case VmType.Int32: v = new(r.Read4()); break;
			case VmType.Int64: v = new(r.Read8()); break;
			case VmType.Int256: v = new(r.ReadBigInt()); break;
			case VmType.Bytes16:
				Bytes16 b16 = new();
				b16.Read(r);
				v = new(b16);
				break;
			case VmType.Bytes32:
				Bytes32 b32 = new();
				b32.Read(r);
				v = new(b32);
				break;
			case VmType.Bytes64:
				Bytes64 b64 = new();
				b64.Read(r);
				v = new(b64);
				break;
			case VmType.String:
				v = new(r.ReadSz());
				break;
			case VmType.Array | VmType.Dynamic:
				VmDynamicVariable[] dyn;
				r.ReadArray(out dyn);
				v = new VmDynamicVariable { type = type, data = dyn };
				break;
			case VmType.Array | VmType.Bytes:
				byte[][] bytesArr;
				r.ReadArray(out bytesArr);
				v = new VmDynamicVariable { type = type, data = bytesArr };
				break;
			case VmType.Array | VmType.Struct:
				Int32 arrayLength = 0;
				r.Read4(out arrayLength);
				VmStructSchema? readSchema = null;
				if (schema == null)
				{
					readSchema = CarbonBlob.New<VmStructSchema>(r);
					if (readSchema.Value.fields.Length > 0)
						schema = readSchema;
				}
				VmDynamicStruct[] structs = new VmDynamicStruct[arrayLength];
				for (int i = 0; i != arrayLength; ++i)
				{
					structs[i] = new VmDynamicStruct();
					if (schema != null)
						structs[i].Read(schema.Value, r);
					else
						structs[i].Read(r);
				}
				if (readSchema != null)
					v = new VmDynamicVariable(new VmStructArray { schema = readSchema.Value, structs = structs });
				else
					v = new VmDynamicVariable(new VmStructArray { structs = structs });
				break;
			case VmType.Array | VmType.Int8:
				byte[] i8;
				r.ReadArray(out i8);
				v = new VmDynamicVariable { type = type, data = i8 };
				break;
			case VmType.Array | VmType.Int16:
				Int16[] i16;
				r.ReadArray16(out i16);
				v = new VmDynamicVariable { type = type, data = i16 };
				break;
			case VmType.Array | VmType.Int32:
				Int32[] i32;
				r.ReadArray32(out i32);
				v = new VmDynamicVariable { type = type, data = i32 };
				break;
			case VmType.Array | VmType.Int64:
				Int64[] i64;
				r.ReadArray64(out i64);
				v = new VmDynamicVariable { type = type, data = i64 };
				break;
			case VmType.Array | VmType.Int256:
				BigInteger[] i256 = r.ReadArrayBigInt();
				v = new VmDynamicVariable { type = type, data = i256 };
				break;
			case VmType.Array | VmType.Bytes16:
				Bytes16[] b16a;
				r.ReadArray(out b16a);
				v = new VmDynamicVariable { type = type, data = b16a };
				break;
			case VmType.Array | VmType.Bytes32:
				Bytes32[] b32a;
				r.ReadArray(out b32a);
				v = new VmDynamicVariable { type = type, data = b32a };
				break;
			case VmType.Array | VmType.Bytes64:
				Bytes64[] b64a;
				r.ReadArray(out b64a);
				v = new VmDynamicVariable { type = type, data = b64a };
				break;
			case VmType.Array | VmType.String:
				string[] sarr = r.ReadArraySz();
				v = new VmDynamicVariable { type = type, data = sarr };
				break;
			default:
				v = new();
				throw new InvalidDataException("Unknown VmDynamicVariable type");
		}
	}
	public static bool Write(VmType type, VmDynamicVariable v, VmStructSchema? schema, BinaryWriter w)
	{
		if (v.type != type)
		{
			VmDynamicVariable error = new VmDynamicVariable(type);
			Write(type, error, schema, w);
			return false;
		}

		//handle this before the switch a it's the only one that permits a null data
		if (type == VmType.Dynamic)
		{
			if (v.data == null)
			{
				w.Write1((VmType)(VmType.Array | VmType.Dynamic));
				w.Write4(0);
				return true;
			}
			w.Write((VmDynamicVariable)v.data);
			return true;
		}

		Throw.If(v.data == null, "invalid object");

		switch (type)
		{
			case VmType.Bytes: w.WriteArray((byte[])v.data!); return true;
			case VmType.Struct:
				VmDynamicStruct s = (VmDynamicStruct)v.data!;
				if (schema != null)
					return s.Write(schema.Value, w);
				else
					s.Write(w);
				return true;
			case VmType.Int8: w.Write1((byte)v.data!); return true;
			case VmType.Int16: w.Write2((Int16)v.data!); return true;
			case VmType.Int32: w.Write4((Int32)v.data!); return true;
			case VmType.Int64: w.Write8((Int64)v.data!); return true;
			case VmType.Int256: w.WriteBigInt((BigInteger)v.data!); return true;
			case VmType.Bytes16: w.Write((Bytes16)v.data!); return true;
			case VmType.Bytes32: w.Write((Bytes32)v.data!); return true;
			case VmType.Bytes64: w.Write((Bytes64)v.data!); return true;
			case VmType.String: w.WriteSz((string)v.data!); return true;
			case VmType.Array | VmType.Dynamic: w.WriteArray((VmDynamicVariable[])v.data!); return true;
			case VmType.Array | VmType.Bytes: w.WriteArray((byte[][])v.data!); return true;
			case VmType.Array | VmType.Struct:
				VmStructArray sa = (VmStructArray)v.data!;
				w.Write4(sa.structs.Length);
				bool ok = true;
				if (schema == null)
				{
					w.Write(sa.schema);
					if (sa.schema.fields.Length > 0)
						schema = sa.schema;
				}
				for (int i = 0; i != sa.structs.Length; ++i)
				{
					if (schema != null)
						ok &= sa.structs[i].Write(schema.Value, w);
					else
						w.Write(sa.structs[i]);
				}
				return ok;
			case VmType.Array | VmType.Int8: w.WriteArray((byte[])v.data!); return true;
			case VmType.Array | VmType.Int16: w.WriteArray16((Int16[])v.data!); return true;
			case VmType.Array | VmType.Int32: w.WriteArray32((Int32[])v.data!); return true;
			case VmType.Array | VmType.Int64: w.WriteArray64((Int64[])v.data!); return true;
			case VmType.Array | VmType.Int256: w.WriteArrayBigInt((BigInteger[])v.data!); return true;
			case VmType.Array | VmType.Bytes16: w.WriteArray((Bytes16[])v.data!); return true;
			case VmType.Array | VmType.Bytes32: w.WriteArray((Bytes32[])v.data!); return true;
			case VmType.Array | VmType.Bytes64: w.WriteArray((Bytes64[])v.data!); return true;
			case VmType.Array | VmType.String: w.WriteArraySz((string[])v.data!); return true;
			default:
				VmAssert(false, "invalid VmDynamicVariable");
				return false;
		}
	}
}

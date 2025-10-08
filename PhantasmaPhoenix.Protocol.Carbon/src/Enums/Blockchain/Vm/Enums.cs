namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

public enum VmType
{
	Dynamic = 0,
	Array = 1,
	Bytes = 1 << 1,
	Struct = 2 << 1,
	Int8 = 3 << 1,
	Int16 = 4 << 1,
	Int32 = 5 << 1,
	Int64 = 6 << 1,
	Int256 = 7 << 1,
	Bytes16 = 8 << 1,
	Bytes32 = 9 << 1,
	Bytes64 = 10 << 1,
	String = 11 << 1,

	Array_Dynamic = Array | Dynamic,
	Array_Bytes = Array | Bytes,
	Array_Struct = Array | Struct,
	Array_Int8 = Array | Int8,
	Array_Int16 = Array | Int16,
	Array_Int32 = Array | Int32,
	Array_Int64 = Array | Int64,
	Array_Int256 = Array | Int256,
	Array_Bytes16 = Array | Bytes16,
	Array_Bytes32 = Array | Bytes32,
	Array_Bytes64 = Array | Bytes64,
	Array_String = Array | String,
};

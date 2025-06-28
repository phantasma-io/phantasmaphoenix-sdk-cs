namespace PhantasmaPhoenix.Cryptography;

public enum AddressKind
{
	Invalid = 0,
	User = 1,
	System = 2,
	Interop = 3,
}

public enum SignatureKind
{
	None,
	Ed25519,
	ECDSA
}

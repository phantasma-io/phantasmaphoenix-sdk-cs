namespace PhantasmaPhoenix.NFT;

public interface IRom
{
	bool IsEmpty();
	(bool, string?) HasParsingError();
	string GetName();
	string GetDescription();
	DateTime GetDate();
}

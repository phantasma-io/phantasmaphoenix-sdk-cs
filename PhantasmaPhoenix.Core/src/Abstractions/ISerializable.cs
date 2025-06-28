namespace PhantasmaPhoenix.Core;

public interface ISerializable
{
    public void SerializeData(BinaryWriter writer);
    public void UnserializeData(BinaryReader reader);
}

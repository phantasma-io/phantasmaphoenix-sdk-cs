using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Cryptography.Extensions;

namespace PhantasmaPhoenix.Protocol;

public struct Event
{
	public EventKind Kind { get; private set; }
	public Address Address { get; private set; }
	public string Contract { get; private set; }
	public string? Name { get; private set; }
	public byte[] Data { get; private set; }

	public Event(EventKind kind, Address address, string contract, byte[] data, string? name = null)
	{
		this.Kind = kind;
		this.Address = address;
		this.Contract = contract;
		this.Data = data;
		this.Name = name;
	}

	public override string ToString()
	{
		if (Name == null)
			return $"{Kind}/{Contract} @ {Address}: {Data.ToHex()}";

		return $"{Kind}/{Contract} - {Name} @ {Address}: {Data.ToHex()}";
	}

	public void Serialize(BinaryWriter writer)
	{
		writer.Write((byte)this.Kind);
		writer.WriteAddress(this.Address);
		writer.WriteVarString(this.Contract);
		writer.WriteByteArray(this.Data);
		if (this.Kind == EventKind.Custom_V2)
			writer.WriteVarString(this.Name ?? "");
	}

	public static Event Unserialize(BinaryReader reader)
	{
		var kind = (EventKind)reader.ReadByte();
		var address = reader.ReadAddress();
		var contract = reader.ReadVarString();
		var data = reader.ReadByteArray();
		var readerPosition = reader.BaseStream.Position;

		if (kind != EventKind.Custom_V2)
		{
			return new Event(kind, address, contract, data);
		}

		try
		{
			var name = reader.ReadVarString();
			return new Event(kind, address, contract, data, name);
		}
		catch (Exception e)
		{
			reader.BaseStream.Position = readerPosition;
			return new Event(kind, address, contract, data);
		}
	}

	public static Event Unserialize(byte[] bytes)
	{
		using var stream = new MemoryStream(bytes);
		using var reader = new BinaryReader(stream);
		return Event.Unserialize(reader);
	}

	public override bool Equals(object obj)
	{
		if (obj is not Event)
		{
			return false;
		}
		else if (obj is Event other)
		{
			if (this.Data == null && other.Data == null)
			{
				if (this.Name == null && other.Name == null)
				{
					return this.Kind == other.Kind && this.Address.Text == other.Address.Text &&
					       this.Contract == other.Contract;
				}

				return this.Kind == other.Kind && this.Address.Text == other.Address.Text &&
				       this.Contract == other.Contract && this.Name.Equals(other.Name);
			}

			if (this.Name == null && other.Name == null)
			{
				return this.Kind == other.Kind && this.Address.Text == other.Address.Text &&
				       this.Contract == other.Contract && this.Data.SequenceEqual(other.Data);
			}

			return this.Kind == other.Kind && this.Address.Text == other.Address.Text &&
			       this.Contract == other.Contract && this.Data.SequenceEqual(other.Data) &&
			       this.Name.Equals(other.Name);
		}

		return base.Equals(obj);
	}
}

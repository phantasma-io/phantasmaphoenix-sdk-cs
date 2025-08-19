using PhantasmaPhoenix.Core;

namespace PhantasmaPhoenix.Protocol.Carbon;

public static class CarbonBlob
{
	public static T New<T>(BinaryReader r) where T : ICarbonBlob, new()
	{
		T t = new T();
		t.Read(r);
		return t;
	}
	public static T New<T>(Stream s, bool allowTrailingBytes = false) where T : ICarbonBlob, new()
	{
		using (var r = new BinaryReader(s))
		{
			T v = New<T>(r);
			if (!allowTrailingBytes)
			{
				if (s.CanSeek)
					Throw.If(s.Position != s.Length, "unexpected trailing bytes");
			}
			return v;
		}
	}
	public static T New<T>(byte[] bytes, long offset) where T : ICarbonBlob, new()
	{
		return New<T>(bytes, false, offset);
	}
	public static T New<T>(byte[] bytes, bool allowTrailingBytes = false, long offset = 0) where T : ICarbonBlob, new()
	{
		using (var s = new MemoryStream(bytes))
		{
			if (offset > 0)
				s.Position = offset;
			return New<T>(s, allowTrailingBytes);
		}
	}
	public static byte[] Serialize<T>(T carbonBlob) where T : ICarbonBlob, new()
	{
		using MemoryStream buffer = new();
		using BinaryWriter w = new(buffer);
		w.Write(carbonBlob);

		return buffer.ToArray();
	}
}

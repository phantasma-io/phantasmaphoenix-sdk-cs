namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

public struct VmDynamicStruct : ICarbonBlob
{
	public VmNamedDynamicVariable[] fields;//NOTE: fields *must* be sorted by name

	public VmDynamicVariable? this[string key]
	{
		get => GetValue(new SmallString(key));
		//	set => SetValue(key, value);
	}
	public VmDynamicVariable? this[SmallString key]
	{
		get => GetValue(key);
		//	set => SetValue(key, value);
	}
	public static VmDynamicStruct New(VmStructSchema schema, byte[] bytes)
	{
		using var s = new MemoryStream(bytes);
		using var r = new BinaryReader(s);
		var result = new VmDynamicStruct();
		result.Read(schema, r);
		return result;
	}

	public VmDynamicVariable? GetValue(SmallString key)
	{
		return fields.Where(x => x.name.data == key.data).Select(x => x.value).Cast<VmDynamicVariable?>().FirstOrDefault();
	}

	//void Erase(VmDynamicVariable);

	public void Write(BinaryWriter w)
	{
		if (fields.Length > 1)
		{
			Array.Sort(fields, (a, b) => a.name.CompareTo(b.name));
		}
		w.WriteArray(fields);
	}
	public void Read(BinaryReader r)
	{
		r.ReadArray(out fields);
		//todo - throw if fields are not sorted by name
		Array.Sort(fields, (a, b) => a.name.CompareTo(b.name));
	}

	public bool Write(VmStructSchema schema, BinaryWriter w)
	{
		bool ok = true;
		int fieldsFound = 0;
		foreach (var f in schema.fields)
		{
			VmNamedDynamicVariable? inStruct = fields.Where(x => x.name.data == f.name.data).Cast<VmNamedDynamicVariable?>().FirstOrDefault();
			if (inStruct != null)
			{
				inStruct.Value.value.Write(f.schema, w);
				fieldsFound++;
			}
			else
			{
				VmDynamicVariable error = new VmDynamicVariable(f.schema.type);
				error.Write(f.schema, w);
				ok = false;
			}
		}
		if (0 == (schema.flags & VmStructSchema.Flags.DynamicExtras))
			return ok;
		if (fieldsFound == schema.fields.Length && schema.fields.Length == fields.Length) // we wrote exactly every field, so there can't possibly be extras
		{
			w.Write4(0);
			return ok;
		}
		var extras = new List<VmNamedDynamicVariable>();
		foreach (var f in fields)
		{
			VmNamedVariableSchema? inSchema = schema.fields.Where(x => x.name.data == f.name.data).Cast<VmNamedVariableSchema?>().FirstOrDefault();
			if (inSchema == null)
				extras.Add(f);
		}
		w.WriteArray(extras.ToArray());
		return ok;
	}

	public void Read(VmStructSchema schema, BinaryReader r)
	{
		if (schema.fields.Length == 0)
		{
			fields = Array.Empty<VmNamedDynamicVariable>();
		}
		else
		{
			fields = new VmNamedDynamicVariable[schema.fields.Length];
			for (int i = 0; i != schema.fields.Length; ++i)
			{
				fields[i].name = schema.fields[i].name;
				fields[i].value.Read(schema.fields[i].schema, r);
			}
		}
		if (0 == (schema.flags & VmStructSchema.Flags.DynamicExtras))//no extras
		{
			if (0 == (schema.flags & VmStructSchema.Flags.IsSorted))//not pre-sorted
			{
				Array.Sort(fields, (a, b) => a.name.CompareTo(b.name));
			}
			return;
		}

		VmNamedDynamicVariable[] extras;
		r.ReadArray(out extras);
		fields = fields.Concat(extras).ToArray();
		Array.Sort(fields, (a, b) => a.name.CompareTo(b.name));
	}
}

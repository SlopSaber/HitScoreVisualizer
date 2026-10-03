using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace HitScoreVisualizer.Utilities.Json;

internal class Vector3Converter : JsonConverter
{
	private readonly bool useGlobalDefaults;

	internal Vector3Converter(bool useGlobalDefaults = true)
	{
		this.useGlobalDefaults = useGlobalDefaults;
	}

	public override bool CanConvert(Type objectType)
	{
		return objectType == typeof(Vector3?) || objectType == typeof(Vector3);
	}

	public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
	{
		var t = serializer.Deserialize(reader);
		if (t == null)
		{
			return objectType == typeof(Vector3) ? default(Vector3) : null!;
		}

		if (useGlobalDefaults)
		{
			return JsonConvert.DeserializeObject<Vector3>(t.ToString());
		}
		using var text = new StringReader(t.ToString());
		using var nestedReader = new JsonTextReader(text);
		var nestedSerializer = JsonSerializer.Create();
		nestedSerializer.CheckAdditionalContent = true;
		return nestedSerializer.Deserialize<Vector3>(nestedReader);
	}

	public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
	{
		if (value is Vector3 v)
		{
			writer.WriteStartObject();
			writer.WritePropertyName("x");
			writer.WriteValue(v.x);
			writer.WritePropertyName("y");
			writer.WriteValue(v.y);
			writer.WritePropertyName("z");
			writer.WriteValue(v.z);
			writer.WriteEndObject();
		}
		else
		{
			writer.WriteNull();
		}
	}
}

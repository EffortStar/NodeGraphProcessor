using System;
using UnityEditor;

// Warning, the current serialization code does not handle unity objects
// in play mode outside of the editor (because of JsonUtility)

namespace GraphProcessor
{
	[Serializable]
	public struct JsonElement
	{
		public string type;
		public string jsonDatas;

		public override string ToString()
		{
			return "type: " + type + " | JSON: " + jsonDatas;
		}
	}

	public static class JsonSerializer
	{
		public static JsonElement Serialize(object obj)
		{
			var elem = new JsonElement
			{
				type = obj.GetType().AssemblyQualifiedName,
				jsonDatas = EditorJsonUtility.ToJson(obj)
			};
			return elem;
		}

		public static T Deserialize<T>(JsonElement e)
		{
			if (typeof(T) != Type.GetType(e.type))
				throw new ArgumentException("Deserializing type is not the same than Json element type");

			var obj = Activator.CreateInstance<T>();
			EditorJsonUtility.FromJsonOverwrite(e.jsonDatas, obj);
			return obj;
		}

		public static JsonElement SerializeNode(BaseNode node)
		{
			return Serialize(node);
		}

		public static BaseNode DeserializeNode(JsonElement e)
		{
			try
			{
				var baseNodeType = Type.GetType(e.type);

				if (e.jsonDatas == null)
					return null;

				var node = Activator.CreateInstance(baseNodeType) as BaseNode;
				EditorJsonUtility.FromJsonOverwrite(e.jsonDatas, node);
				return node;
			}
			catch
			{
				return null;
			}
		}
	}
}
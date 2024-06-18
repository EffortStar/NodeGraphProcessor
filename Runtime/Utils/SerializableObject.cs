using System;
using UnityEngine;
using System.Globalization;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GraphProcessor
{
	// Warning: this class only support the serialization of UnityObject and primitive
	[Serializable]
	public class SerializableObject
	{
		[Serializable]
		class ObjectWrapper
		{
			public UnityEngine.Object value;
		}

		public string serializedType;
		public string serializedName;
		public string serializedValue;

		public object value;

		public SerializableObject(object value, Type type, string name = null)
		{
			this.value = value;
			serializedName = name;
			serializedType = type.AssemblyQualifiedName;
		}

		public void Deserialize()
		{
			if (string.IsNullOrEmpty(serializedType))
			{
				Debug.LogError("Can't deserialize the object from null type");
				return;
			}

			var type = Type.GetType(serializedType)!;

			if (type.IsPrimitive)
			{
				value = string.IsNullOrEmpty(serializedValue)
					? Activator.CreateInstance(type)
					: Convert.ChangeType(serializedValue, type, CultureInfo.InvariantCulture);
			}
			else if (typeof(UnityEngine.Object).IsAssignableFrom(type))
			{
				var obj = new ObjectWrapper();
				JsonUtility.FromJsonOverwrite(serializedValue, obj);
				value = obj.value;
			}
			else if (type == typeof(string))
				value = serializedValue.Length > 1 ? serializedValue.Substring(1, serializedValue.Length - 2).Replace("\\\"", "\"") : "";
			else
			{
				try
				{
					value = Activator.CreateInstance(type);
					JsonUtility.FromJsonOverwrite(serializedValue, value);
				}
				catch (Exception e)
				{
					Debug.LogError(e);
					Debug.LogError("Can't serialize type " + serializedType);
				}
			}
		}

		public void Serialize()
		{
			if (value == null)
				return;

			serializedType = value.GetType().AssemblyQualifiedName;

			if (value.GetType().IsPrimitive)
				serializedValue = Convert.ToString(value, CultureInfo.InvariantCulture);
			else if (value is UnityEngine.Object v) //type is a unity object
			{
				if (v == null)
					return;

				var wrapper = new ObjectWrapper { value = v };
				serializedValue = JsonUtility.ToJson(wrapper);
			}
			else if (value is string @string)
				serializedValue = "\"" + @string.Replace("\"", "\\\"") + "\"";
			else
			{
				try
				{
					serializedValue = JsonUtility.ToJson(value);
					if (string.IsNullOrEmpty(serializedValue))
						throw new Exception();
				}
				catch
				{
					Debug.LogError("Can't serialize type " + serializedType);
				}
			}
		}
	}
}
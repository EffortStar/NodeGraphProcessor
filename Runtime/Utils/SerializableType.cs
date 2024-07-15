using System;
using System.Collections.Generic;
using UnityEngine;

namespace GraphProcessor
{
	[Serializable]
	public class SerializableType : ISerializationCallbackReceiver
	{
		private static Dictionary<string, Type> s_typeCache = new();
		private static Dictionary<Type, string> s_typeNameCache = new();

		[SerializeField]
		private string serializedType;

		[NonSerialized]
		public Type Type;

		public SerializableType(Type t) => Type = t;

		public void OnAfterDeserialize()
		{
			if (string.IsNullOrEmpty(serializedType)) return;
			if (s_typeCache.TryGetValue(serializedType, out Type)) return;
			Type = Type.GetType(serializedType);
			s_typeCache[serializedType] = Type;
		}

		public void OnBeforeSerialize()
		{
			if (Type == null) return;
			if (s_typeNameCache.TryGetValue(Type, out serializedType)) return;
			serializedType = Type.AssemblyQualifiedName;
			s_typeNameCache[Type] = serializedType;
		}
	}
}
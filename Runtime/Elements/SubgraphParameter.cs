using System;
using UnityEditor.Experimental.GraphView;
using UnityEditor.Graphs;

namespace GraphProcessor
{
	[Serializable]
	public sealed class SubgraphParameter
	{
		public string Guid; // unique id to keep track of the parameter
		public string Name;
		public Direction Direction;
		public string Type;

		public string ShortType => GetValueType()?.Name;

		public void Initialize(string name, Type type, Direction direction)
		{
			Guid = System.Guid.NewGuid().ToString(); // Generated once and unique per parameter
			Name = name;
			Direction = direction;
			Type = SerializedType.ToString(type);
		}
		
		public Type GetValueType() => SerializedType.FromString(Type);

		public static bool operator ==(SubgraphParameter param1, SubgraphParameter param2)
		{
			if (ReferenceEquals(param1, null) && ReferenceEquals(param2, null))
				return true;
			if (ReferenceEquals(param1, param2))
				return true;
			if (ReferenceEquals(param1, null))
				return false;
			if (ReferenceEquals(param2, null))
				return false;

			return param1.Equals(param2);
		}

		public static bool operator !=(SubgraphParameter param1, SubgraphParameter param2) => !(param1 == param2);

		public bool Equals(SubgraphParameter parameter) => Guid == parameter.Guid;

		public override bool Equals(object obj) => obj != null && Equals(obj as SubgraphParameter);

		public override int GetHashCode() => Guid.GetHashCode();

		public SubgraphParameter Clone()
		{
			var clonedParam = (SubgraphParameter)Activator.CreateInstance(GetType());
			clonedParam.Guid = Guid;
			clonedParam.Name = Name;
			clonedParam.Direction = Direction;
			clonedParam.Type = Type;

			return clonedParam;
		}
	}
}
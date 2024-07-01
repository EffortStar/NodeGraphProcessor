using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GraphProcessor
{
	[Serializable]
	internal sealed class NodeGraphState
	{
		[Serializable]
		public struct StateValue
		{
			public string Guid;
			public Vector3 Position;
			public float Scale;
		}

		[SerializeField]
		private List<StateValue> _values = new();

		[NonSerialized]
		private static Dictionary<string, StateValue> s_values;

		[NonSerialized]
		private static NodeGraphState s_instance;

		[NonSerialized]
		private static bool s_dirty;

		private static NodeGraphState Instance
		{
			get
			{
				if (s_instance != null)
					return s_instance;
				string path = GetOutputPath();
				if (File.Exists(path))
				{
					try
					{

						s_instance = JsonUtility.FromJson<NodeGraphState>(path);
					}
					catch
					{
						s_instance = new NodeGraphState();
					}
				}
				else
					s_instance = new NodeGraphState();
				s_values ??= new Dictionary<string, StateValue>();
				s_values.Clear();
				if (s_instance._values != null)
				{
					foreach (StateValue v in s_instance._values)
						s_values.Add(v.Guid, v);
				}
				else
				{
					s_instance._values = new List<StateValue>();
				}

				return s_instance;
			}
		}

		private static string GetOutputPath() => Path.GetFullPath(Path.Combine(Application.dataPath, "../", "Library", "NodeGraphState.json"));

		public static bool TryGetStateValue(Object graph, out StateValue value, out string guid)
		{
			_ = Instance;
			AssetDatabase.TryGetGUIDAndLocalFileIdentifier(graph, out guid, out long _);
			return s_values.TryGetValue(guid, out value);
		}

		public static void UpdateStateValue(StateValue value)
		{
			s_values[value.Guid] = value;
			s_dirty = true;
		}

		public static void SaveToDisk()
		{
			if (!s_dirty) return;
			Instance._values = s_values.Values.ToList();
			File.WriteAllText(GetOutputPath(), JsonUtility.ToJson(Instance));
			s_dirty = false;
		}
	}
}
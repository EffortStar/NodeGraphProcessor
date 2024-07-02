using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.IO;
using System.Reflection;
using UnityEditor.Experimental.GraphView;

namespace GraphProcessor
{
	public static class NodeProvider
	{
		public const string ObsoleteNodePrefix = "[DEPRECATED]";
		
		[Flags]
		public enum NodeFlags
		{
			None = 0,
			Obsolete = 1 << 0,
			Prototype = 1 << 1
		}
		
		private class AllCachedNodeDetails
		{
			public readonly Dictionary<Type, CachedNodeDetails> NodesByType = new();
		}

		private class CachedNodeDetails
		{
			public IEnumerable<string> MenuPaths => _menusPaths ?? Enumerable.Empty<string>();

			public List<PortDescription> PortDescriptions
			{
				get
				{
					if (_portDescriptions != null)
						return _portDescriptions;
					_portDescriptions = new List<PortDescription>();
					ProvideNodePortCreationDescription(NodeType, _portDescriptions);
					return _portDescriptions;
				}
			}

			public MonoScript Script
			{
				get
				{
					if (_script != null)
						return _script;
					string nodeTypeName = NodeType.Name;
					_script = FindScriptFromClassName(nodeTypeName);
					// Try find the class name with Node name at the end
					if (_script == null)
						_script = FindScriptFromClassName($"{nodeTypeName}Node");
					return _script;
				}
			}

			public MonoScript ViewScript
			{
				get
				{
					if (_viewScript != null)
						return _viewScript;
					string nodeEditorTypeName = NodeEditorType.Name;
					_viewScript = FindScriptFromClassName(nodeEditorTypeName);
					if (_viewScript == null)
						_viewScript = FindScriptFromClassName($"{nodeEditorTypeName}View");
					if (_viewScript == null)
						_viewScript = FindScriptFromClassName($"{nodeEditorTypeName}NodeView");
					return _viewScript;
				}
			}

			public readonly Type NodeType;
			public readonly bool Obsolete;
			public bool Prototype;
			public Type NodeEditorType;

			private List<string> _menusPaths;
			private HashSet<Type> _compatibleGraphTypes;
			private MonoScript _script;
			private MonoScript _viewScript;
			private List<PortDescription> _portDescriptions;

			public CachedNodeDetails(Type nodeType)
			{
				NodeType = nodeType;
				Obsolete = Attribute.IsDefined(nodeType, typeof(ObsoleteAttribute));
			}

			public void AddMenuPath(string path)
			{
				_menusPaths ??= new List<string>();

				if (Obsolete)
				{
					int lastSlash = path.LastIndexOf('/');
					if (lastSlash >= 0)
						path = path[(lastSlash + 1)..];
					path = $"Deprecated/{ObsoleteNodePrefix} {path}";
				}
				
				_menusPaths.Add(path);
			}

			public void AddCompatibleGraphType(Type type)
			{
				_compatibleGraphTypes ??= new HashSet<Type>();
				_compatibleGraphTypes.Add(type);
			}

			public bool IsCompatibleWithGraphType(Type graphType)
			{
				while (true)
				{
					if (graphType == null || _compatibleGraphTypes == null)
					{
						return true;
					}

					if (_compatibleGraphTypes.Contains(graphType))
					{
						return true;
					}

					if (graphType.BaseType == typeof(BaseGraph))
					{
						return false;
					}

					graphType = graphType.BaseType;
				}
			}
		}

		private static readonly AllCachedNodeDetails NodeCache = new();

		private static void BuildNodeCache()
		{
			NodeCache.NodesByType.Add(typeof(BaseNode), new CachedNodeDetails(typeof(BaseNode)));
			foreach (Type nodeType in TypeCache.GetTypesDerivedFrom<BaseNode>())
			{
				NodeCache.NodesByType.Add(nodeType, new CachedNodeDetails(nodeType));
			}

			// Collect node menu details
			foreach (Type type in TypeCache.GetTypesWithAttribute<NodeMenuItemAttribute>())
			{
				if (!NodeCache.NodesByType.TryGetValue(type, out CachedNodeDetails cache))
				{
					Debug.LogError($"{type} was decorated with {nameof(NodeMenuItemAttribute)} but it doesn't inherit from {nameof(BaseNode)}.");
					continue;
				}

				foreach (NodeMenuItemAttribute attribute in type.GetCustomAttributes<NodeMenuItemAttribute>())
				{
					if (!string.IsNullOrEmpty(attribute.menuTitle))
					{
						cache.AddMenuPath(attribute.menuTitle);
					}

					if (attribute.onlyCompatibleWithGraph != null)
					{
						cache.AddCompatibleGraphType(attribute.onlyCompatibleWithGraph);
					}
				}
			}

			// Collect views for nodes
			foreach (Type type in TypeCache.GetTypesWithAttribute<NodeCustomEditorAttribute>())
			{
				foreach (NodeCustomEditorAttribute attribute in type.GetCustomAttributes<NodeCustomEditorAttribute>())
				{
					Type nodeType = attribute.nodeType;
					if (!NodeCache.NodesByType.TryGetValue(nodeType, out CachedNodeDetails cachedDetails))
					{
						Debug.LogError($"{type} was decorated with {nameof(NodeCustomEditorAttribute)} but its target, {nodeType}, doesn't inherit from {nameof(BaseNode)}.");
						continue;
					}

					if (cachedDetails.NodeEditorType != null)
					{
						Debug.LogError($"{type} targets, {nodeType}, with {nameof(NodeCustomEditorAttribute)}. Which already was used by {cachedDetails.NodeEditorType}.");
						continue;
					}

					cachedDetails.NodeEditorType = type;
				}
			}
			
			// Collect prototype nodes
			foreach (Type type in TypeCache.GetTypesWithAttribute<PrototypeNodeAttribute>())
			{
				if (!NodeCache.NodesByType.TryGetValue(type, out CachedNodeDetails cache))
				{
					Debug.LogError($"{type} was decorated with {nameof(PrototypeNodeAttribute)} but it doesn't inherit from {nameof(BaseNode)}.");
					continue;
				}

				cache.Prototype = true;
			}
		}

		public struct PortDescription
		{
			public Type nodeType;
			public Type portType;
			public bool isInput;
			public string portFieldName;
			public string portIdentifier;
			public string portDisplayName;
		}

		static NodeProvider() => BuildNodeCache();

		private static void ProvideNodePortCreationDescription(Type nodeType, List<PortDescription> descriptions)
		{
			if (nodeType.IsAbstract || nodeType.IsGenericType)
				return;

			var node = (BaseNode)Activator.CreateInstance(nodeType);
			node.InitializePorts();
			node.UpdateAllPorts();

			foreach (NodePort p in node.inputPorts)
				AddPort(p, true);
			foreach (NodePort p in node.outputPorts)
				AddPort(p, false);
			return;

			void AddPort(NodePort p, bool input)
			{
				descriptions.Add(new PortDescription
				{
					nodeType = nodeType,
					portType = p.portData.displayType ?? p.fieldInfo.FieldType,
					isInput = input,
					portFieldName = p.fieldName,
					portDisplayName = p.portData.displayName ?? p.fieldName,
					portIdentifier = p.portData.identifier,
				});
			}
		}

		private static MonoScript FindScriptFromClassName(string className)
		{
			string[] scriptGUIDs = AssetDatabase.FindAssets($"t:script {className}");

			if (scriptGUIDs.Length == 0)
				return null;

			foreach (string scriptGUID in scriptGUIDs)
			{
				string assetPath = AssetDatabase.GUIDToAssetPath(scriptGUID);
				var script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);

				if (script != null && string.Equals(className, Path.GetFileNameWithoutExtension(assetPath), StringComparison.OrdinalIgnoreCase))
					return script;
			}

			return null;
		}

		public static Type GetNodeViewTypeFromType(Type nodeType)
		{
			while (true)
			{
				if (NodeCache.NodesByType.TryGetValue(nodeType!, out CachedNodeDetails details) && details.NodeEditorType != null)
				{
					return details.NodeEditorType;
				}

				if (nodeType == typeof(BaseNode))
				{
					return null;
				}

				nodeType = nodeType.BaseType;
			}
		}

		public static IEnumerable<(string path, Type type)> GetNodeMenuEntries(BaseGraph graph = null)
		{
			Type graphType = graph == null ? null : graph.GetType();

			foreach ((Type nodeType, CachedNodeDetails details) in NodeCache.NodesByType)
			{
				if (nodeType.IsAbstract)
					continue;

				if (!details.IsCompatibleWithGraphType(graphType))
					continue;

				foreach (string menuPath in details.MenuPaths)
					yield return (menuPath, nodeType);
			}
		}

		public static MonoScript GetNodeViewScript(Type type)
			=> NodeCache.NodesByType.TryGetValue(type, out CachedNodeDetails details) ? details.ViewScript : null;

		public static MonoScript GetNodeScript(Type type)
			=> NodeCache.NodesByType.TryGetValue(type, out CachedNodeDetails details) ? details.Script : null;

		public static IEnumerable<PortDescription> GetEdgeCreationNodeMenuEntry(PortView portView, BaseGraph graph = null)
		{
			Type graphType = graph == null ? null : graph.GetType();

			foreach ((_, CachedNodeDetails details) in NodeCache.NodesByType)
			{
				if (!details.IsCompatibleWithGraphType(graphType))
					continue;

				foreach (PortDescription port in details.PortDescriptions)
				{
					if (!IsPortCompatible(port))
						continue;
					yield return port;
				}
			}

			yield break;

			bool IsPortCompatible(PortDescription description)
			{
				if ((portView.direction == Direction.Input && description.isInput) || (portView.direction == Direction.Output && !description.isInput))
					return false;

				if (!BaseGraph.TypesAreConnectable(description.portType, portView.portType))
					return false;

				return true;
			}
		}

		public static NodeFlags GetNodeFlags(Type nodeType)
		{
			var flags = NodeFlags.None;
			if (NodeCache.NodesByType.TryGetValue(nodeType, out CachedNodeDetails details))
			{
				if (details.Obsolete)
					flags |= NodeFlags.Obsolete;
				if (details.Prototype)
					flags |= NodeFlags.Prototype;
			}
			return flags;
		}
	}
}
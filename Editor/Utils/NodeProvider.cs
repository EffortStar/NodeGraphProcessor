using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.IO;
using System.Reflection;
using JetBrains.Annotations;
using UnityEditor.Experimental.GraphView;
using Object = UnityEngine.Object;

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
			Prototype = 1 << 1,
			HasInfo = 1 << 2,
			SubgraphIncompatible = 1 << 3,
			//
			Striped = Obsolete | Prototype | SubgraphIncompatible
		}

		[Flags]
		public enum PortFlags
		{
			None = 0,
			Obsolete = 1 << 0
		}
		
		private sealed class AllCachedNodeDetails
		{
			public readonly Dictionary<Type, CachedNodeDetails> NodesByType = new();
		}

		private sealed class CachedNodeDetails
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
			
			public IReadOnlyCollection<Type> CompatibleGraphTypes => _compatibleGraphTypes;

			public readonly Type NodeType;
			public NodeFlags Flags;
			public Type NodeEditorType;

			private Color? _color;
			private List<string> _menusPaths;
			private HashSet<Type> _compatibleGraphTypes;
			private MonoScript _script;
			private MonoScript _viewScript;
			private List<PortDescription> _portDescriptions;
			private Dictionary<string, ConfigureNode> _configurationMethods;

			public CachedNodeDetails(Type nodeType)
			{
				NodeType = nodeType;
				Flags = Attribute.IsDefined(nodeType, typeof(ObsoleteAttribute)) ? NodeFlags.Obsolete : NodeFlags.None;
			}

			public void AddMenuPath(string path)
			{
				_menusPaths ??= new List<string>();

				if ((Flags & NodeFlags.Obsolete) != 0)
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
					if (graphType == null || CompatibleGraphTypes == null)
					{
						return true;
					}

					if (CompatibleGraphTypes.Contains(graphType))
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

			public void SetColor(Color color) => _color = color;

			public bool TryGetColor(out Color color)
			{
				color = _color.GetValueOrDefault();
				return _color.HasValue;
			}

			public void AddConfiguration(string menuTitle, ConfigureNode configuration)
			{
				_configurationMethods ??= new Dictionary<string, ConfigureNode>();
				_configurationMethods.Add(menuTitle, configuration);
			}

			public ConfigureNode GetConfigurationOrNull(string menuPath)
				=> _configurationMethods?.TryGetValue(menuPath, out ConfigureNode configuration) ?? false ? configuration : null;
		}

		private sealed class NodeCreationDetails
		{
			private readonly Dictionary<Type, List<(Type nodeType, MethodInfo initializeNode)>> _dragAndDropLookup = new();

			public NodeCreationDetails()
			{
				foreach (Type type in TypeCache.GetTypesDerivedFrom(typeof(ICreateNodeFrom<>)))
				{
					if (type.IsAbstract)
					{
						continue;
					}

					if (!type.IsSubclassOf(typeof(BaseNode)))
					{
						Debug.LogError($"{type} inherits from {typeof(ICreateNodeFrom<>).Name}. This interface can only be implemented on types inheriting from {nameof(BaseNode)}.");
						continue;
					}
					
					foreach (Type i in type.GetInterfaces())
					{
						if (!i.IsGenericType || i.GetGenericTypeDefinition() != typeof(ICreateNodeFrom<>))
						{
							continue;
						}
						

						Type genericArgumentType = i.GetGenericArguments()[0];
						MethodInfo initializeFunction = type.GetMethod(
							nameof(ICreateNodeFrom<Object>.InitializeNodeFromObject),
							BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
							null, new[] { typeof(BaseGraph), genericArgumentType }, null
						);

						if (!_dragAndDropLookup.TryGetValue(genericArgumentType, out var list))
						{
							_dragAndDropLookup.Add(genericArgumentType, list = new());
						}

						// We only add the type that implements the interface, not it's children
						if (IsNearestImplementation(initializeFunction!, type))
						{
							list.Add((type, initializeFunction));
						}
					}
					
				}
			}

			private static bool IsNearestImplementation(MethodInfo initializeFunction, Type type)
			{
				if (initializeFunction.DeclaringType == type)
				{
					return true;
				}

				// This could probably be better implemented, we just check if the parent of the type is abstract or generic,
				// if so, then it probably cannot create a node.
				if (type.BaseType!.IsAbstract || type.BaseType.IsGenericType)
				{
					return true;
				}

				return false;
			}

			public IEnumerable<KeyValuePair<Type, List<(Type nodeType, MethodInfo initializeNode)>>> DragAndDropTypes => _dragAndDropLookup;
			
			public bool TryGetFromAssetType(Type assetType, out List<(Type nodeType, MethodInfo initializeNode)> result) => _dragAndDropLookup.TryGetValue(assetType, out result);
		}

		private static readonly AllCachedNodeDetails s_nodeCache = new();
		[CanBeNull] private static NodeCreationDetails _nodeCreationDetails = null;

		private static void BuildNodeCache()
		{
			s_nodeCache.NodesByType.Add(typeof(BaseNode), new CachedNodeDetails(typeof(BaseNode)));
			foreach (Type nodeType in TypeCache.GetTypesDerivedFrom<BaseNode>())
			{
				s_nodeCache.NodesByType.Add(nodeType, new CachedNodeDetails(nodeType));
			}

			// Collect node menu details
			foreach (Type type in TypeCache.GetTypesWithAttribute<NodeMenuItemAttribute>())
			{
				if (!s_nodeCache.NodesByType.TryGetValue(type, out CachedNodeDetails cache))
				{
					Debug.LogError($"{type} was decorated with {nameof(NodeMenuItemAttribute)} but it doesn't inherit from {nameof(BaseNode)} or is abstract.");
					continue;
				}

				foreach (NodeMenuItemAttribute attribute in type.GetCustomAttributes<NodeMenuItemAttribute>())
				{
					if (!string.IsNullOrEmpty(attribute.MenuTitle))
					{
						cache.AddMenuPath(attribute.MenuTitle);
					}

					if (attribute.OnlyCompatibleWithGraph != null)
					{
						cache.AddCompatibleGraphType(attribute.OnlyCompatibleWithGraph);
					}

					if (!attribute.SubgraphSupport)
					{
						cache.Flags |= NodeFlags.SubgraphIncompatible;
					}
				}
			}
			
			// Collect node menu details
			foreach (Type type in TypeCache.GetTypesWithAttribute<GenericNodeMenuItemAttribute>())
			{
				if (!s_nodeCache.NodesByType.ContainsKey(type))
				{
					Debug.LogError($"{type} was decorated with {nameof(GenericNodeMenuItemAttribute)} but it doesn't inherit from {nameof(BaseNode)} or is abstract.");
					continue;
				}
				
				foreach (GenericNodeMenuItemAttribute attribute in type.GetCustomAttributes<GenericNodeMenuItemAttribute>())
				{
					Type genericType;
					try
					{
						genericType = type.MakeGenericType(attribute.TypeParameters);
					}
					catch (Exception)
					{
						Debug.LogError(
							$"{nameof(GenericNodeMenuItemAttribute)} on {type.Name} could not be solidified " +
							$"using types {string.Join(',', attribute.TypeParameters.Select(t => t.Name))}"
						);
						continue;
					}

					CachedNodeDetails cache;
					if (!s_nodeCache.NodesByType.TryAdd(genericType, cache = new CachedNodeDetails(genericType)))
					{
						Debug.LogError($"{nameof(GenericNodeMenuItemAttribute)}: Multiple nodes of type {genericType} were attempted to be registered from {type}.");
						continue;
					}
					
					if (!string.IsNullOrEmpty(attribute.MenuTitle))
					{
						cache.AddMenuPath(attribute.MenuTitle);
					}

					foreach (NodeMenuItemAttribute menuAttribute in type.GetCustomAttributes<NodeMenuItemAttribute>())
					{
						if (menuAttribute.OnlyCompatibleWithGraph != null)
						{
							cache.AddCompatibleGraphType(menuAttribute.OnlyCompatibleWithGraph);
						}

						if (!menuAttribute.SubgraphSupport)
						{
							cache.Flags |= NodeFlags.SubgraphIncompatible;
						}
					}
				}
			}
			
			foreach (MethodInfo methodInfo in TypeCache.GetMethodsWithAttribute<NodeMenuItemProducerAttribute>())
			{
				if (!methodInfo.IsStatic)
				{
					Debug.LogError($"{methodInfo} was decorated with {nameof(NodeMenuItemProducerAttribute)} but it was not static.");
					continue;
				}

				if (methodInfo.Invoke(null, null) is not ProducedNode[] producedNodes)
				{
					Debug.LogError($"{methodInfo} was decorated with {nameof(NodeMenuItemProducerAttribute)} but does not return {nameof(ProducedNode)}[].");
					continue;
				}

				foreach (ProducedNode producedNode in producedNodes)
				{
					Type type = producedNode.NodeType;
					if (!s_nodeCache.NodesByType.TryGetValue(type, out CachedNodeDetails cache))
					{
						// Late detail creation for generic types.
						if (type.IsGenericType && !type.IsAbstract && typeof(BaseNode).IsAssignableFrom(type))
						{
							s_nodeCache.NodesByType.Add(type, cache = new CachedNodeDetails(type));
						}
						else
						{
							Debug.LogError($"{type} was decorated with {nameof(NodeMenuItemAttribute)} but it doesn't inherit from {nameof(BaseNode)} or is abstract.");
							continue;
						}
					}
					
					cache.AddMenuPath(producedNode.MenuTitle);
					cache.AddConfiguration(producedNode.MenuTitle, producedNode.Configure);
				}
			}

			// Collect views for nodes
			foreach (Type type in TypeCache.GetTypesWithAttribute<NodeCustomEditorAttribute>())
			{
				foreach (NodeCustomEditorAttribute attribute in type.GetCustomAttributes<NodeCustomEditorAttribute>())
				{
					Type nodeType = attribute.nodeType;
					if (!s_nodeCache.NodesByType.TryGetValue(nodeType, out CachedNodeDetails cachedDetails))
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
				if (!s_nodeCache.NodesByType.TryGetValue(type, out CachedNodeDetails cache))
				{
					Debug.LogError($"{type} was decorated with {nameof(PrototypeNodeAttribute)} but it doesn't inherit from {nameof(BaseNode)}.");
					continue;
				}

				cache.Flags |= NodeFlags.Prototype;
			}
			
			// Collect prototype nodes
			foreach (Type type in TypeCache.GetTypesWithAttribute<NodeInfoAttribute>())
			{
				if (!s_nodeCache.NodesByType.TryGetValue(type, out CachedNodeDetails cache))
				{
					Debug.LogError($"{type} was decorated with {nameof(NodeInfoAttribute)} but it doesn't inherit from {nameof(BaseNode)}.");
					continue;
				}

				cache.Flags |= NodeFlags.HasInfo;
			}
			
			foreach (Type type in TypeCache.GetTypesWithAttribute<NodeColorAttribute>())
			{
				if (!s_nodeCache.NodesByType.TryGetValue(type, out CachedNodeDetails cache))
				{
					Debug.LogError($"{type} was decorated with {nameof(NodeColorAttribute)} but it doesn't inherit from {nameof(BaseNode)}.");
					continue;
				}

				cache.SetColor(type.GetCustomAttribute<NodeColorAttribute>(true).Color);
			}
		}
		
		

		public struct PortDescription
		{
			public Type NodeType;
			public Type PortType;
			public BaseGraph SubgraphContext;
			public bool IsInput;
			public string PortFieldName;
			public string PortIdentifier;
			public string PortDisplayName;
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
					NodeType = nodeType,
					PortType = p.portData.displayType ?? p.fieldInfo.FieldType,
					IsInput = input,
					PortFieldName = p.fieldName,
					PortDisplayName = p.portData.EditorOnly.DisplayName ?? p.fieldName,
					PortIdentifier = p.portData.identifier,
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
				if (s_nodeCache.NodesByType.TryGetValue(nodeType!, out CachedNodeDetails details) && details.NodeEditorType != null)
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

		public static IEnumerable<(string path, Type type, ConfigureNode configuration)> GetNodeMenuEntries(BaseGraph graph = null)
		{
			Type graphType = graph == null ? null : graph.GetType();

			foreach ((Type nodeType, CachedNodeDetails details) in s_nodeCache.NodesByType)
			{
				if (nodeType.IsAbstract)
					continue;

				if (!details.IsCompatibleWithGraphType(graphType))
					continue;

				foreach (string menuPath in details.MenuPaths)
					yield return (menuPath, nodeType, details.GetConfigurationOrNull(menuPath));
			}
		}

		public static IEnumerable<Type> GetGraphTypeRequirementFromType(Type nodeType)
		{
			if (!s_nodeCache.NodesByType.TryGetValue(nodeType, out CachedNodeDetails details))
			{
				Debug.LogWarning($"{nodeType} was not present in cache");
				return Enumerable.Empty<Type>();
			}
			return details.CompatibleGraphTypes ?? Enumerable.Empty<Type>();
		}

		public static MonoScript GetNodeViewScript(Type type)
			=> s_nodeCache.NodesByType.TryGetValue(type, out CachedNodeDetails details) ? details.ViewScript : null;

		public static MonoScript GetNodeScript(Type type)
			=> s_nodeCache.NodesByType.TryGetValue(type, out CachedNodeDetails details) ? details.Script : null;

		public static bool TryGetNodeColor(Type type, out Color color)
		{
			if (s_nodeCache.NodesByType.TryGetValue(type, out CachedNodeDetails details))
			{
				return details.TryGetColor(out color);
			}

			color = default;
			return false;
		}

		public static IEnumerable<PortDescription> GetEdgeCreationNodeMenuEntry(PortView portView, BaseGraph graph = null)
		{
			Type graphType = graph == null ? null : graph.GetType();

			foreach ((_, CachedNodeDetails details) in s_nodeCache.NodesByType)
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
				if ((portView.direction == Direction.Input && description.IsInput) || (portView.direction == Direction.Output && !description.IsInput))
					return false;

				if (!BaseGraph.TypesAreConnectable(description.PortType, portView.portType))
					return false;

				return true;
			}
		}

		public static NodeFlags GetNodeFlags(Type nodeType) => s_nodeCache.NodesByType.TryGetValue(nodeType, out CachedNodeDetails details) ? details.Flags : NodeFlags.None;

		public static bool TryGetNodeFromDragAndDroppedAsset(BaseGraph graph, Object asset, Vector2 mousePos, out BaseNode node)
		{
			_nodeCreationDetails ??= new NodeCreationDetails();
			Type assetType = asset.GetType();
			foreach ((Type type, List<(Type nodeType, MethodInfo initializeNode)> list) in _nodeCreationDetails.DragAndDropTypes)
			{
				if (assetType == type)
				{
					if (TryCreate(list, out node))
					{
						return true;
					}
				}
			}
			
			foreach ((Type type, List<(Type nodeType, MethodInfo initializeNode)> list) in _nodeCreationDetails.DragAndDropTypes)
			{
				if (assetType.IsSubclassOf(type))
				{
					if (TryCreate(list, out node))
					{
						return true;
					}
				}
			}
			
			node = null;
			return false;


			bool TryCreate(List<(Type nodeType, MethodInfo initializeNode)> nodes, [CanBeNull] out BaseNode node)
			{
				foreach ((Type nodeType, MethodInfo initializeNode) in nodes)
				{
					Type graphType = graph.GetType();
					if (!s_nodeCache.NodesByType.TryGetValue(nodeType, out var details) || !details.IsCompatibleWithGraphType(graphType))
					{
						node = null;
						return false;
					}

					try
					{
						node = BaseNode.CreateFromType(nodeType, mousePos);
						if ((bool)initializeNode.Invoke(node, new object[] { graph, asset }))
						{
							return true;
						}
					}
					catch (Exception exception)
					{
						Debug.LogException(exception);
					}
				}

				node = null;
				return false;
			}
		}
	}
}
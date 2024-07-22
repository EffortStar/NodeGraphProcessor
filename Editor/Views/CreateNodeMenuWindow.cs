using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using UnityEditor;

namespace GraphProcessor
{
	// TODO: replace this by the new UnityEditor.Searcher package
	internal class CreateNodeMenuWindow : ScriptableObject, ISearchWindowProvider
	{
		private BaseGraphView _graphView;
		private EditorWindow _window;
		private Texture2D _icon;
		private EdgeView _edgeFilter;
		private PortView _inputPortView;
		private PortView _outputPortView;

		public void Initialize(BaseGraphView graphView, EditorWindow window, EdgeView edgeFilter = null)
		{
			_graphView = graphView;
			_window = window;
			_edgeFilter = edgeFilter;
			_inputPortView = (PortView)edgeFilter?.input;
			_outputPortView = (PortView)edgeFilter?.output;

			// Transparent icon to trick search window into indenting items
			if (_icon == null)
				_icon = new Texture2D(1, 1);
			_icon.SetPixel(0, 0, new Color(0, 0, 0, 0));
			_icon.Apply();
		}

		private void OnDestroy()
		{
			if (_icon != null)
			{
				DestroyImmediate(_icon);
				_icon = null;
			}
		}

		public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
		{
			var tree = new List<SearchTreeEntry>
			{
				new SearchTreeGroupEntry(new GUIContent("Create Node"), 0),
			};

			if (_edgeFilter == null)
				CreateStandardNodeMenu(tree);
			else
				CreateEdgeNodeMenu(tree);

			return tree;
		}

		private void CreateStandardNodeMenu(List<SearchTreeEntry> tree)
		{
			// Sort menu by alphabetical order and submenus
			IOrderedEnumerable<(string path, Type type)> nodeEntries = _graphView.FilterCreateNodeMenuEntries().OrderBy(k => k.path);
			var titlePaths = new HashSet<string>();
			AddNodeEntries(tree, nodeEntries, titlePaths);
			IEnumerable<(string, BaseGraph)> subgraphEntries = GetSubgraphEntries();
			AddNodeEntries(tree, subgraphEntries, titlePaths);
		}

		private IEnumerable<(string path, BaseGraph subgraph)> GetSubgraphEntries()
		{
			Type type = _graphView.graph.GetType();
			foreach (BaseGraph subgraph in AssetDatabase.FindAssets($"t:{nameof(BaseGraph)}")
				         .Select(path => AssetDatabase.LoadAssetAtPath<BaseGraph>(AssetDatabase.GUIDToAssetPath(path)))
				         .Where(g => g != null && g.IsSubgraph))
			{
				if (!subgraph.GetType().IsAssignableFrom(type))
					continue;

				yield return ($"Subgraph/{SubgraphNode.GetNameFromSubgraph(subgraph)}", subgraph);
			}
		}

		private void AddNodeEntries<T>(List<SearchTreeEntry> tree, IEnumerable<(string path, T type)> nodeEntries, HashSet<string> titlePaths)
		{
			foreach ((string nodePath, T type) in nodeEntries)
			{
				string nodeName = nodePath;
				var level = 0;
				string[] parts = nodePath.Split('/');

				if (parts.Length > 1)
				{
					level++;
					nodeName = parts[^1];
					var fullTitleAsPath = "";

					for (var i = 0; i < parts.Length - 1; i++)
					{
						string title = parts[i];
						fullTitleAsPath += title;
						level = i + 1;

						// Add section title if the node is in subcategory
						if (!titlePaths.Contains(fullTitleAsPath))
						{
							tree.Add(new SearchTreeGroupEntry(new GUIContent(title))
							{
								level = level
							});
							titlePaths.Add(fullTitleAsPath);
						}
					}
				}

				tree.Add(new SearchTreeEntry(new GUIContent(nodeName, _icon))
				{
					level = level + 1,
					userData = type
				});
			}
		}

		private void CreateEdgeNodeMenu(List<SearchTreeEntry> tree)
		{
			PortView originPortView = _inputPortView ?? _outputPortView;
			IEnumerable<NodeProvider.PortDescription> entries = NodeProvider.GetEdgeCreationNodeMenuEntry(originPortView, _graphView.graph);

			var titlePaths = new HashSet<string>();

			(string path, Type type)[] nodePaths = NodeProvider.GetNodeMenuEntries(_graphView.graph).ToArray();

			tree.Add(new SearchTreeEntry(new GUIContent("Relay", _icon))
			{
				level = 1,
				userData = new NodeProvider.PortDescription
				{
					PortType = typeof(object),
					IsInput = _inputPortView != null,
					PortFieldName = _inputPortView != null ? nameof(SimplifiedRelayNode.Out) : nameof(SimplifiedRelayNode.In),
					PortDisplayName = _inputPortView != null ? "Out" : "In",
					NodeType = typeof(SimplifiedRelayNode)
				}
			});

			IOrderedEnumerable<(NodeProvider.PortDescription port, string path)> sortedMenuItems =
				entries.Select(port => (port, nodePaths.FirstOrDefault(kp => kp.type == port.NodeType).path))
					.OrderBy(e => e.path);

			AddPortEntries(tree, sortedMenuItems, titlePaths);

			bool isInput = _inputPortView != null;
			var subgraphEntries = GetSubgraphEntries().SelectMany(e => GetParametersFromSubgraph(e.subgraph, e.path));
			AddPortEntries(tree, subgraphEntries, titlePaths);
			return;

			IEnumerable<(NodeProvider.PortDescription port, string path)> GetParametersFromSubgraph(BaseGraph graph, string path)
			{
				foreach (SubgraphParameter parameter in graph.SubgraphParameters)
				{
					if (parameter.Direction != (isInput ? ParameterDirection.Output : ParameterDirection.Input))
						continue;
					
					if (!BaseGraph.TypesAreConnectable(parameter.GetValueType(), originPortView.portType))
						continue;
					
					yield return (new NodeProvider.PortDescription
					{
						IsInput = isInput,
						PortType = parameter.GetValueType(),
						PortFieldName = isInput ? nameof(SubgraphNode.Outputs) : nameof(SubgraphNode.Inputs),
						PortIdentifier = parameter.Guid,
						PortDisplayName = parameter.Name,
						SubgraphContext = graph,
						NodeType = typeof(SubgraphNode)
					}, path);
				}
			}
		}

		private void AddPortEntries(
			List<SearchTreeEntry> tree,
			IEnumerable<(NodeProvider.PortDescription port, string path)> sortedMenuItems,
			HashSet<string> titlePaths
		)
		{
			// Sort menu by alphabetical order and submenus
			foreach ((NodeProvider.PortDescription port, string path) in sortedMenuItems)
			{
				// Ignore the node if it's not in the create menu
				if (string.IsNullOrEmpty(path))
					continue;

				string nodeName = path;
				var level = 0;
				string[] parts = path.Split('/');

				if (parts.Length > 1)
				{
					level++;
					nodeName = parts[^1];
					var fullTitleAsPath = "";

					for (var i = 0; i < parts.Length - 1; i++)
					{
						string title = parts[i];
						fullTitleAsPath += title;
						level = i + 1;

						// Add section title if the node is in subcategory
						if (!titlePaths.Contains(fullTitleAsPath))
						{
							tree.Add(new SearchTreeGroupEntry(new GUIContent(title))
							{
								level = level
							});
							titlePaths.Add(fullTitleAsPath);
						}
					}
				}

				tree.Add(new SearchTreeEntry(new GUIContent($"{nodeName}:  {port.PortDisplayName}", _icon))
				{
					level = level + 1,
					userData = port
				});
			}
		}

		// Node creation when validate a choice
		public bool OnSelectEntry(SearchTreeEntry searchTreeEntry, SearchWindowContext context)
		{
			// window to graph position
			VisualElement windowRoot = _window.rootVisualElement;
			Vector2 windowMousePosition = windowRoot.ChangeCoordinatesTo(windowRoot.parent, context.screenMousePosition - _window.position.position);
			Vector2 graphMousePosition = _graphView.contentViewContainer.WorldToLocal(windowMousePosition);

			BaseNode node;
			switch (searchTreeEntry.userData)
			{
				case Type t:
					node = BaseNode.CreateFromType(t, graphMousePosition);
					break;
				case NodeProvider.PortDescription description when description.SubgraphContext != null:
				{
					var subgraphNode = BaseNode.CreateFromType<SubgraphNode>(graphMousePosition);
					subgraphNode.Subgraph = description.SubgraphContext;
					node = subgraphNode;
					break;
				}
				case NodeProvider.PortDescription description:
					node = BaseNode.CreateFromType(description.NodeType, graphMousePosition);
					break;
				case BaseGraph graph:
				{
					var subgraphNode = BaseNode.CreateFromType<SubgraphNode>(graphMousePosition);
					subgraphNode.Subgraph = graph;
					node = subgraphNode;
					break;
				}
				default:
					throw new NotImplementedException();
			}

			_graphView.RegisterCompleteObjectUndo("Added " + node.GetType().Name);
			BaseNodeView view = _graphView.AddNode(node);

			if (searchTreeEntry.userData is NodeProvider.PortDescription desc)
			{
				PortView targetPort = view.GetPortViewFromFieldName(desc.PortFieldName, desc.PortIdentifier);
				if (_inputPortView == null)
					_graphView.Connect(_outputPortView, targetPort);
				else
					_graphView.Connect(targetPort, _inputPortView);
			}

			return true;
		}
	}
}
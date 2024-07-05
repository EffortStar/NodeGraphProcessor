using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using UnityEditor;

namespace GraphProcessor
{
	// TODO: replace this by the new UnityEditor.Searcher package
	internal class CreateNodeMenuWindow : ScriptableObject, ISearchWindowProvider
	{
		private BaseGraphView graphView;
		private EditorWindow window;
		private Texture2D icon;
		private EdgeView edgeFilter;
		private PortView inputPortView;
		private PortView outputPortView;

		public void Initialize(BaseGraphView graphView, EditorWindow window, EdgeView edgeFilter = null)
		{
			this.graphView = graphView;
			this.window = window;
			this.edgeFilter = edgeFilter;
			this.inputPortView = edgeFilter?.input as PortView;
			this.outputPortView = edgeFilter?.output as PortView;

			// Transparent icon to trick search window into indenting items
			if (icon == null)
				icon = new Texture2D(1, 1);
			icon.SetPixel(0, 0, new Color(0, 0, 0, 0));
			icon.Apply();
		}

		private void OnDestroy()
		{
			if (icon != null)
			{
				DestroyImmediate(icon);
				icon = null;
			}
		}

		public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
		{
			var tree = new List<SearchTreeEntry>
			{
				new SearchTreeGroupEntry(new GUIContent("Create Node"), 0),
			};

			if (edgeFilter == null)
				CreateStandardNodeMenu(tree);
			else
				CreateEdgeNodeMenu(tree);

			return tree;
		}

		private void CreateStandardNodeMenu(List<SearchTreeEntry> tree)
		{
			// Sort menu by alphabetical order and submenus
			IOrderedEnumerable<(string path, Type type)> nodeEntries = graphView.FilterCreateNodeMenuEntries().OrderBy(k => k.path);
			var titlePaths = new HashSet<string>();

			foreach ((string path, Type type) nodeMenuItem in nodeEntries)
			{
				string nodePath = nodeMenuItem.path;
				string nodeName = nodePath;
				var level = 0;
				string[] parts = nodePath.Split('/');

				if (parts.Length > 1)
				{
					level++;
					nodeName = parts[parts.Length - 1];
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

				tree.Add(new SearchTreeEntry(new GUIContent(nodeName, icon))
				{
					level = level + 1,
					userData = nodeMenuItem.type
				});
			}
		}

		private void CreateEdgeNodeMenu(List<SearchTreeEntry> tree)
		{
			IEnumerable<NodeProvider.PortDescription> entries = NodeProvider.GetEdgeCreationNodeMenuEntry((edgeFilter.input ?? edgeFilter.output) as PortView, graphView.graph);

			var titlePaths = new HashSet<string>();

			(string path, Type type)[] nodePaths = NodeProvider.GetNodeMenuEntries(graphView.graph).ToArray();

			tree.Add(new SearchTreeEntry(new GUIContent("Relay", icon))
			{
				level = 1,
				userData = new NodeProvider.PortDescription
				{
					portType = typeof(object),
					isInput = inputPortView != null,
					portFieldName = inputPortView != null ? nameof(SimplifiedRelayNode.Out) : nameof(SimplifiedRelayNode.In),
					portDisplayName = inputPortView != null ? "Out" : "In",
					nodeType = typeof(SimplifiedRelayNode)
				}
			});

			IOrderedEnumerable<(NodeProvider.PortDescription port, string path)> sortedMenuItems = 
				entries.Select(port => (port, nodePaths.FirstOrDefault(kp => kp.type == port.nodeType).path))
					.OrderBy(e => e.path);

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

				tree.Add(new SearchTreeEntry(new GUIContent($"{nodeName}:  {port.portDisplayName}", icon))
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
			VisualElement windowRoot = window.rootVisualElement;
			Vector2 windowMousePosition = windowRoot.ChangeCoordinatesTo(windowRoot.parent, context.screenMousePosition - window.position.position);
			Vector2 graphMousePosition = graphView.contentViewContainer.WorldToLocal(windowMousePosition);

			Type nodeType = searchTreeEntry.userData is Type ? (Type)searchTreeEntry.userData : ((NodeProvider.PortDescription)searchTreeEntry.userData).nodeType;

			graphView.RegisterCompleteObjectUndo("Added " + nodeType);
			BaseNodeView view = graphView.AddNode(BaseNode.CreateFromType(nodeType, graphMousePosition));

			if (searchTreeEntry.userData is NodeProvider.PortDescription desc)
			{
				PortView targetPort = view.GetPortViewFromFieldName(desc.portFieldName, desc.portIdentifier);
				if (inputPortView == null)
					graphView.Connect(targetPort, outputPortView);
				else
					graphView.Connect(inputPortView, targetPort);
			}

			return true;
		}
	}
}
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.Experimental.GraphView;
using System.Linq;
using System;
using System.IO;
using UnityEditor.SceneManagement;
using JetBrains.Annotations;
using UnityEditor.UIElements;
using Status = UnityEngine.UIElements.DropdownMenuAction.Status;
using Object = UnityEngine.Object;

namespace GraphProcessor
{
	/// <summary>
	/// Base class to write a custom view for a node
	/// </summary>
	public class BaseGraphView : GraphView, IDisposable
	{
		private const string CreateSubgraphKey = "create subgraph";

		/// <summary>
		/// Graph that owns of the node
		/// </summary>
		public BaseGraph graph;

		/// <summary>
		/// Connector listener that will create the edges between ports
		/// </summary>
		public BaseEdgeConnectorListener ConnectorListener;

		/// <summary>
		/// List of all node views in the graph
		/// </summary>
		/// <typeparam name="BaseNodeView"></typeparam>
		/// <returns></returns>
		public readonly List<BaseNodeView> NodeViews = new();

		/// <summary>
		/// Dictionary of the node views accessed view the node instance, faster than a Find in the node view list
		/// </summary>
		/// <typeparam name="BaseNode"></typeparam>
		/// <typeparam name="BaseNodeView"></typeparam>
		/// <returns></returns>
		public readonly Dictionary<BaseNode, BaseNodeView> NodeViewsPerNode = new();

		/// <summary>
		/// List of all edge views in the graph
		/// </summary>
		/// <typeparam name="EdgeView"></typeparam>
		/// <returns></returns>
		private readonly List<EdgeView> _edgeViews = new();

		/// <summary>
		/// List of all group views in the graph
		/// </summary>
		/// <typeparam name="GroupView"></typeparam>
		/// <returns></returns>
		private readonly List<GroupView> _groupViews = new();

#if UNITY_2020_1_OR_NEWER
		/// <summary>
		/// List of all sticky note views in the graph
		/// </summary>
		/// <typeparam name="StickyNoteView"></typeparam>
		/// <returns></returns>
		private readonly List<StickyNoteView> _stickyNoteViews = new();
#endif

		/// <summary>
		/// List of all stack node views in the graph
		/// </summary>
		/// <typeparam name="BaseStackNodeView"></typeparam>
		/// <returns></returns>
		private readonly List<BaseStackNodeView> _stackNodeViews = new();
		private readonly Dictionary<Type, PinnedElementView> _pinnedElements = new();
		private readonly CreateNodeMenuWindow _createNodeMenu;

		/// <summary>
		/// Triggered just after the graph is initialized
		/// </summary>
		public event Action Initialized;

		// Safe event relay from BaseGraph (safe because you are sure to always point on a valid BaseGraph
		// when one of these events is called), a graph switch can occur between two call tho
		/// <summary>
		/// Same event than BaseGraph.onSubgraphParameterListChanged
		/// Safe event (not triggered in case the graph is null).
		/// </summary>
		public event Action onSubgraphParameterListChanged;

		/// <summary>
		/// Object to handle nodes that shows their UI in the inspector.
		/// </summary>
		protected NodeInspectorObject NodeInspector
		{
			get
			{
				if (graph.nodeInspectorReference == null)
					graph.nodeInspectorReference = CreateNodeInspectorObject();
				return graph.nodeInspectorReference as NodeInspectorObject;
			}
		}

		public SerializedObject SerializedGraph { get; private set; }

		private NodeGraphState.StateValue _viewState;
		private readonly BaseGraphWindow _window;
		private Dictionary<string, BaseNode> _lastCopiedNodesMap;

		public BaseGraphView(BaseGraphWindow window)
		{
			_window = window;
			serializeGraphElements = SerializeGraphElementsCallback;
			canPasteSerializedData = CanPasteSerializedDataCallback;
			unserializeAndPaste = UnserializeAndPasteCallback;
			graphViewChanged = GraphViewChangedCallback;
			viewTransformChanged = ViewTransformChangedCallback;
			elementResized = ElementResizedCallback;

			RegisterCallback<KeyDownEvent>(KeyDownCallback);
			RegisterCallback<DragPerformEvent>(DragPerformedCallback);
			RegisterCallback<DragUpdatedEvent>(DragUpdatedCallback);
			RegisterCallback<MouseDownEvent>(MouseDownCallback);
			RegisterCallback<MouseUpEvent>(MouseUpCallback);

			InitializeManipulators();

			SetupZoom(0.05f, 2f);

			Undo.undoRedoPerformed += ReloadView;

			_createNodeMenu = ScriptableObject.CreateInstance<CreateNodeMenuWindow>();
			_createNodeMenu.Initialize(this, window);
			
			hierarchy.Add(new Toolbar { name = "graph-view-toolbar" });
		}

		protected virtual NodeInspectorObject CreateNodeInspectorObject()
		{
			var inspector = ScriptableObject.CreateInstance<NodeInspectorObject>();
			inspector.name = "Node Inspector";
			inspector.hideFlags = HideFlags.HideAndDontSave ^ HideFlags.NotEditable;

			return inspector;
		}

		#region Callbacks

		protected override bool canCopySelection
			=> selection.Any(e => e is BaseNodeView or GroupView);

		protected override bool canCutSelection
			=> selection.Any(e => e is BaseNodeView or GroupView);

		private string SerializeGraphElementsCallback(IEnumerable<GraphElement> elements)
		{
			var data = new CopyPasteHelper();

			foreach (BaseNodeView nodeView in elements.OfType<BaseNodeView>())
			{
				data.copiedNodes.Add(JsonSerializer.SerializeNode(nodeView.NodeTarget));
				foreach (NodePort port in nodeView.NodeTarget.AllPorts)
				{
					if (port.IsVertical)
					{
						foreach (SerializableEdge edge in port.Edges)
							data.copiedEdges.Add(JsonSerializer.Serialize(edge));
					}
				}
			}

			foreach (GroupView groupView in elements.OfType<GroupView>())
				data.copiedGroups.Add(JsonSerializer.Serialize(groupView.Group));

			foreach (EdgeView edgeView in elements.OfType<EdgeView>())
				data.copiedEdges.Add(JsonSerializer.Serialize(edgeView.SerializedEdge));

			return JsonUtility.ToJson(data, true);
		}

		private bool CanPasteSerializedDataCallback(string serializedData)
		{
			try
			{
				return JsonUtility.FromJson(serializedData, typeof(CopyPasteHelper)) != null;
			}
			catch
			{
				return false;
			}
		}

		private void UnserializeAndPasteCallback(string operationName, string serializedData)
		{
			bool offset = operationName != CreateSubgraphKey;

			ClearSelection();

			RegisterCompleteObjectUndo(operationName);

			var data = JsonUtility.FromJson<CopyPasteHelper>(serializedData);

			Dictionary<string, BaseNode> copiedNodesMap = _lastCopiedNodesMap = new Dictionary<string, BaseNode>();

			foreach (JsonElement serializedNode in data.copiedNodes)
			{
				BaseNode node = JsonSerializer.DeserializeNode(serializedNode);

				if (node == null)
					continue;

				string sourceGUID = node.GUID;
				graph.nodesPerGUID.TryGetValue(sourceGUID, out BaseNode sourceNode);
				//Call OnNodeCreated on the new fresh copied node
				node.CreatedFromDuplication = true;
				node.OnNodeCreated();

				if (offset)
					node.position += new Vector2(20, 20);

				AddNode(node);

				copiedNodesMap[sourceGUID] = node;

				// Select the new node
				AddToSelection(NodeViewsPerNode[node]);
			}

			foreach (Group group in data.copiedGroups.Select(JsonSerializer.Deserialize<Group>))
			{
				if (offset)
					group.position.position += new Vector2(20, 20);
				GroupView groupView = AddGroup(group);
				AddToSelection(groupView);
			}

			foreach (JsonElement serializedEdge in data.copiedEdges)
			{
				var edge = JsonSerializer.Deserialize<SerializableEdge>(serializedEdge);

				edge.Deserialize(graph, logWarnings: false);
				edge.RemapNodes(graph, copiedNodesMap);
				if (edge.ToNode == null || edge.FromNode == null ||
				    // Logic to protect SubGraphs.
				    !graph.nodesPerGUID.ContainsKey(edge.ToNode.GUID) || !graph.nodesPerGUID.ContainsKey(edge.FromNode.GUID))
				{
					continue;
				}

				// We avoid to break the graph by replacing unique connections:
				if (edge.ToPort.Edges.Count > 0 && !edge.ToPort.AllowMultipleEdges ||
				    edge.FromPort.Edges.Count > 0 && !edge.FromPort.AllowMultipleEdges)
				{
					continue;
				}

				EdgeView edgeView = new()
				{
					userData = edge,
					input = NodeViewsPerNode[edge.ToNode].GetPortView(edge.InputFieldPath, edge.inputPortIdentifier),
					output = NodeViewsPerNode[edge.FromNode].GetPortView(edge.OutputFieldPath, edge.outputPortIdentifier)
				};

				Connect(edgeView);
			}

			contentViewContainer.AddManipulator(new PostPasteNodesManipulator(this));
		}

		private GraphViewChange GraphViewChangedCallback(GraphViewChange changes)
		{
			if (changes.elementsToRemove != null)
			{
				RegisterCompleteObjectUndo("Remove Graph Elements");

				// Destroy priority of objects
				// We need nodes to be destroyed first because we can have a destroy operation that uses node connections
				changes.elementsToRemove.Sort((e1, e2) =>
				{
					return GetPriority(e1).CompareTo(GetPriority(e2));

					int GetPriority(GraphElement e) => e is BaseNodeView ? 0 : 1;
				});

				//Handle ourselves the edge and node remove
				changes.elementsToRemove.RemoveAll(e =>
				{
					switch (e)
					{
						case EdgeView edge:
							Disconnect(edge);
							return true;
						case BaseNodeView nodeView:
							// For vertical nodes, we need to delete them ourselves as it's not handled by GraphView
							foreach (PortView pv in nodeView.InputPortViews.Concat(nodeView.OutputPortViews))
								if (pv.orientation == Orientation.Vertical)
									foreach (EdgeView edge in pv.GetEdges().ToList())
										Disconnect(edge);

							NodeInspector.NodeViewRemoved(nodeView);
							try
							{
								nodeView.OnRemoved();
							}
							catch (Exception ex)
							{
								Debug.LogException(ex);
							}

							RemoveNode(nodeView.NodeTarget);
							UpdateSerializedProperties();
							RemoveElement(nodeView);
							if (Selection.activeObject == NodeInspector)
								UpdateNodeInspectorSelection();

							SyncSerializedPropertyPaths();
							return true;
						case GroupView group:
							graph.RemoveGroup(group.Group);
							UpdateSerializedProperties();
							RemoveElement(group);
							return true;
						case SubgraphParameterFieldView blackboardField:
							graph.RemoveSubgraphParameter(blackboardField.Parameter);
							UpdateSerializedProperties();
							return true;
						case BaseStackNodeView stackNodeView:
							graph.RemoveStackNode(stackNodeView.stackNode);
							UpdateSerializedProperties();
							RemoveElement(stackNodeView);
							return true;
#if UNITY_2020_1_OR_NEWER
						case StickyNoteView stickyNoteView:
							graph.RemoveStickyNote(stickyNoteView.note);
							UpdateSerializedProperties();
							RemoveElement(stickyNoteView);
							return true;
#endif
					}

					return false;
				});
			}

			return changes;
		}

		private void GraphChangesCallback(GraphChanges changes)
		{
			if (changes.removedEdge != null)
			{
				EdgeView edge = _edgeViews.FirstOrDefault(e => e.SerializedEdge == changes.removedEdge);

				DisconnectView(edge);

				RemoveRelayIfRequiredAfterDelay(changes.removedEdge.FromNode);
				RemoveRelayIfRequiredAfterDelay(changes.removedEdge.ToNode);
			}

			if (changes.removedGroups != null)
			{
				GroupView view = _groupViews.FirstOrDefault(g => g.Group == changes.removedGroups);
				if (view != null)
				{
					RemoveElement(view);
					_groupViews.Remove(view);
				}
			}

			return;

			void RemoveRelayIfRequiredAfterDelay(BaseNode node)
			{
				if (node is not SimplifiedRelayNode relay)
					return;
				schedule.Execute(() => RemoveRelayIfRequired(relay));
			}

			// Deletes redirect nodes if they're found to have no connected edges.
			void RemoveRelayIfRequired(SimplifiedRelayNode relay)
			{
				if (!NodeViewsPerNode.ContainsKey(relay))
					return;

				if (
					relay.InputPorts[0].Edges.Count != 0 ||
					relay.OutputPorts[0].Edges.Count != 0
				)
					return;
				RemoveNode(relay);
			}
		}

		private void ViewTransformChangedCallback(GraphView view)
		{
			if (graph == null) return;
			if (_viewState.Guid != null)
			{
				_viewState.Position = viewTransform.position;
				_viewState.Scale = viewTransform.scale.x;
				NodeGraphState.UpdateStateValue(_viewState);
			}
		}

		private void ElementResizedCallback(VisualElement elem)
		{
			if (elem is GroupView groupView)
				groupView.Group.position.size = groupView.GetPosition().size;
		}

		public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
		{
			var compatiblePorts = new List<Port>();

			compatiblePorts.AddRange(ports.ToList().Where(p =>
			{
				var portView = (PortView)p;

				if (portView.Owner == ((PortView)startPort).Owner)
					return false;

				if (p.direction == startPort.direction)
					return false;

				//Check for type assignability
				if (!BaseGraph.TypesAreConnectable(startPort.portType, p.portType))
					return false;

				//Check if the edge already exists
				if (portView.GetEdges().Any(e => e.input == startPort || e.output == startPort))
					return false;

				return true;
			}));

			return compatiblePorts;
		}

		/// <summary>
		/// Build the contextual menu shown when right clicking inside the graph view
		/// </summary>
		/// <param name="evt"></param>
		public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
		{
			base.BuildContextualMenu(evt);
			BuildGroupContextualMenu(evt, 1);
			BuildStickyNoteContextualMenu(evt, 2);
			BuildViewContextualMenu(evt);
			BuildSubgraphContextualMenu(evt);
			BuildSelectAssetContextualMenu(evt);
			BuildSaveAssetContextualMenu(evt);
			BuildHelpContextualMenu(evt);
		}

		/// <summary>
		/// Add the New Group entry to the context menu
		/// </summary>
		/// <param name="evt"></param>
		protected virtual void BuildGroupContextualMenu(ContextualMenuPopulateEvent evt, int menuPosition = -1)
		{
			if (menuPosition == -1)
				menuPosition = evt.menu.MenuItems().Count;
			Vector2 position = (evt.currentTarget as VisualElement).ChangeCoordinatesTo(contentViewContainer, evt.localMousePosition);
			evt.menu.InsertAction(menuPosition, "Create Group", e => AddSelectionsToGroup(AddGroup(new Group("New Group", position))), DropdownMenuAction.AlwaysEnabled);
		}

		/// <summary>
		/// -Add the New Sticky Note entry to the context menu
		/// </summary>
		/// <param name="evt"></param>
		protected virtual void BuildStickyNoteContextualMenu(ContextualMenuPopulateEvent evt, int menuPosition = -1)
		{
			if (menuPosition == -1)
				menuPosition = evt.menu.MenuItems().Count;
#if UNITY_2020_1_OR_NEWER
			Vector2 position = (evt.currentTarget as VisualElement).ChangeCoordinatesTo(contentViewContainer, evt.localMousePosition);
			evt.menu.InsertAction(menuPosition, "Create Sticky Note", e => AddStickyNote(new StickyNote("Create Note", position)), DropdownMenuAction.AlwaysEnabled);
#endif
		}

		/// <summary>
		/// Add the Save Asset entry to the context menu
		/// </summary>
		/// <param name="evt"></param>
		protected virtual void BuildSubgraphContextualMenu(ContextualMenuPopulateEvent evt)
		{
			evt.menu.AppendAction("Subgraph/Create", e => CreateSubgraph(), CanCreateSubgraphFromElements() ? Status.Normal : Status.Disabled);
			evt.menu.AppendAction("Subgraph/Unpack", e => UnpackSubgraph(), CanUnpackSubgraphFromElements() ? Status.Normal : Status.Disabled);
			return;

			bool CanCreateSubgraphFromElements() => selection.OfType<BaseNodeView>().Any();
			bool CanUnpackSubgraphFromElements() => selection.OfType<SubgraphNodeView>().Any();
		}

		/// <summary>
		/// Add the View entry to the context menu
		/// </summary>
		/// <param name="evt"></param>
		protected virtual void BuildViewContextualMenu(ContextualMenuPopulateEvent evt)
		{
		}

		/// <summary>
		/// Add the Select Asset entry to the context menu
		/// </summary>
		/// <param name="evt"></param>
		protected virtual void BuildSelectAssetContextualMenu(ContextualMenuPopulateEvent evt)
		{
			evt.menu.AppendAction("Select Asset", e => EditorGUIUtility.PingObject(graph), DropdownMenuAction.AlwaysEnabled);
		}

		/// <summary>
		/// Add the Save Asset entry to the context menu
		/// </summary>
		/// <param name="evt"></param>
		protected virtual void BuildSaveAssetContextualMenu(ContextualMenuPopulateEvent evt)
		{
			evt.menu.AppendAction("Save Asset", e =>
			{
				EditorUtility.SetDirty(graph);
				AssetDatabase.SaveAssets();
			}, DropdownMenuAction.AlwaysEnabled);
		}

		/// <summary>
		/// Add the Help entry to the context menu
		/// </summary>
		/// <param name="evt"></param>
		protected void BuildHelpContextualMenu(ContextualMenuPopulateEvent evt)
		{
			evt.menu.AppendAction("Help/Reset Pinned Windows", e =>
			{
				foreach (KeyValuePair<Type, PinnedElementView> kp in _pinnedElements)
					kp.Value.ResetPosition();
			});
			
			evt.menu.AppendAction("Help/Debug Selected Elements", e =>
			{
				foreach (BaseNodeView baseNodeView in selection.OfType<BaseNodeView>())
				{
					BaseNode node = baseNodeView.NodeTarget;
					Debug.Log($"{node} {node.GUID}");
				}
				
				foreach (EdgeView edgeView in selection.OfType<EdgeView>())
				{
					SerializableEdge edge = edgeView.SerializedEdge;
					Debug.Log($"{edge} {edge.GUID}");
				}
			});
		}

		protected virtual void KeyDownCallback(KeyDownEvent e)
		{
			if (e.keyCode == KeyCode.LeftControl)
				return;

			if (e.keyCode == KeyCode.S && (e.commandKey || e.ctrlKey) && !e.shiftKey)
			{
				SaveGraphToDisk();
				e.StopPropagation();
			}

			if (e.keyCode == KeyCode.G && (e.commandKey || e.ctrlKey))
			{
				AddSelectionsToGroup(AddGroup(new Group("New Group")));
				e.StopPropagation();
			}
			else if (NodeViews.Count > 0 && (e.commandKey || e.ctrlKey) && e.altKey)
			{
				//	Node Aligning shortcuts
				switch (e.keyCode)
				{
					case KeyCode.LeftArrow:
						NodeViews[0].AlignToLeft();
						e.StopPropagation();
						break;
					case KeyCode.RightArrow:
						NodeViews[0].AlignToRight();
						e.StopPropagation();
						break;
					case KeyCode.UpArrow:
						NodeViews[0].AlignToTop();
						e.StopPropagation();
						break;
					case KeyCode.DownArrow:
						NodeViews[0].AlignToBottom();
						e.StopPropagation();
						break;
					case KeyCode.C:
						NodeViews[0].AlignToCenter();
						e.StopPropagation();
						break;
					case KeyCode.M:
						NodeViews[0].AlignToMiddle();
						e.StopPropagation();
						break;
				}
			}
		}

		private void MouseUpCallback(MouseUpEvent e)
		{
			schedule.Execute(() =>
			{
				if (DoesSelectionContainsInspectorNodes())
					UpdateNodeInspectorSelection();
			}).ExecuteLater(1);
		}

		private void MouseDownCallback(MouseDownEvent e)
		{
			// When left clicking on the graph (not a node or something else)
			if (e.button == 0)
			{
				// Close all settings windows:
				NodeViews.ForEach(v => v.CloseSettings());
			}

			if (DoesSelectionContainsInspectorNodes())
				UpdateNodeInspectorSelection();
		}

		private bool DoesSelectionContainsInspectorNodes()
		{
			List<ISelectable> selectedNodes = selection.Where(s => s is BaseNodeView).ToList();
			List<ISelectable> selectedNodesNotInInspector = selectedNodes.Except(NodeInspector.selectedNodes).ToList();
			List<ISelectable> nodeInInspectorWithoutSelectedNodes = NodeInspector.selectedNodes.Except(selectedNodes).ToList();

			return selectedNodesNotInInspector.Any() || nodeInInspectorWithoutSelectedNodes.Any();
		}

		private void DragPerformedCallback(DragPerformEvent e)
		{
			Vector2 mousePos = (e.currentTarget as VisualElement).ChangeCoordinatesTo(contentViewContainer, e.localMousePosition);

			// Drag and Drop for elements inside the graph
			if (DragAndDrop.GetGenericData("DragSelection") is List<ISelectable> dragData)
			{
				IEnumerable<SubgraphParameterFieldView> exposedParameterFieldViews = dragData.OfType<SubgraphParameterFieldView>();
				if (exposedParameterFieldViews.Any())
				{
					foreach (SubgraphParameterFieldView paramFieldView in exposedParameterFieldViews)
					{
						RegisterCompleteObjectUndo("Create Parameter Node");
						var paramNode = BaseNode.CreateFromType<ParameterNode>(mousePos);
						paramNode.parameterGUID = paramFieldView.Parameter.Guid;
						AddNode(paramNode);
					}
				}
			}

			// External objects drag and drop
			if (DragAndDrop.objectReferences.Length > 0)
			{
				RegisterCompleteObjectUndo("Create Node From Object(s)");
				foreach (Object obj in DragAndDrop.objectReferences)
				{
					// ReSharper disable once Unity.NoNullPatternMatching
					if (obj is BaseGraph draggedGraph)
					{
						var node = BaseNode.CreateFromType<SubgraphNode>(mousePos);
						node.Subgraph = draggedGraph;
						AddNode(node);
						continue;
					}

					Type objectType = obj.GetType();

					while (objectType != typeof(Object))
					{
						if (NodeProvider.TryGetNodeFromDragAndDroppedAsset(graph, obj, mousePos, out BaseNode createdNode))
						{
							AddNode(createdNode);
							break;
						}

						objectType = objectType!.BaseType;
					}
				}
			}
		}

		private void DragUpdatedCallback(DragUpdatedEvent e)
		{
			var dragData = DragAndDrop.GetGenericData("DragSelection") as List<ISelectable>;
			Object[] dragObjects = DragAndDrop.objectReferences;
			var dragging = false;

			if (dragData != null)
			{
				// Handle drag from exposed parameter view
				if (dragData.OfType<SubgraphParameterFieldView>().Any())
				{
					dragging = true;
				}
			}

			if (dragObjects.Length > 0)
				dragging = true;

			if (dragging)
				DragAndDrop.visualMode = DragAndDropVisualMode.Generic;

			UpdateNodeInspectorSelection();
		}

		#endregion

		#region Initialization

		private void ReloadView()
		{
			// Force the graph to reload his data (Undo have updated the serialized properties of the graph
			// so the one that are not serialized need to be synchronized)
			graph.Deserialize();

			// Get selected nodes
			var selectedNodeGUIDs = new List<string>();
			foreach (ISelectable e in selection)
			{
				if (e is BaseNodeView v && Contains(v))
					selectedNodeGUIDs.Add(v.NodeTarget.GUID);
			}

			// Remove everything
			RemoveNodeViews();
			RemoveEdges();
			RemoveGroups();
#if UNITY_2020_1_OR_NEWER
			RemoveStickyNotes();
#endif
			RemoveStackNodeViews();

			UpdateSerializedProperties();

			// And re-add with new up to date datas
			InitializeNodeViews();
			InitializeEdgeViews();
			InitializeGroups();
			InitializeStickyNotes();
			InitializeStackNodes();

			Reload();

			// Restore selection after re-creating all views
			// selection = nodeViews.Where(v => selectedNodeGUIDs.Contains(v.nodeTarget.GUID)).Select(v => v as ISelectable).ToList();
			foreach (string guid in selectedNodeGUIDs)
			{
				AddToSelection(NodeViews.FirstOrDefault(n => n.NodeTarget.GUID == guid));
			}

			UpdateNodeInspectorSelection();
		}

		public void Initialize(BaseGraph graph)
		{
			if (this.graph != null)
			{
				SaveGraphToDisk();
				// Close pinned windows from old graph:
				ClearGraphElements();
			}

			this.graph = graph;

			UpdateSerializedProperties();

			ConnectorListener = CreateEdgeConnectorListener();

			// When pressing ctrl-s, we save the graph
			EditorSceneManager.sceneSaved += _ => SaveGraphToDisk();
			RegisterCallback<KeyDownEvent>(e =>
			{
				if (e.keyCode == KeyCode.S && e.actionKey && !e.shiftKey)
					SaveGraphToDisk();
			});

			ClearGraphElements();

			InitializeGraphView();
			InitializeNodeViews();
			InitializeEdgeViews();
			InitializeViews();
			InitializeGroups();
			InitializeStickyNotes();
			InitializeStackNodes();

			Initialized?.Invoke();

			InitializeView();
		}

		public void ClearGraphElements()
		{
			RemoveGroups();
			RemoveNodeViews();
			RemoveEdges();
			RemoveStackNodeViews();
			RemovePinnedElementViews();
#if UNITY_2020_1_OR_NEWER
			RemoveStickyNotes();
#endif
		}

		private void UpdateSerializedProperties()
		{
			if (graph == null)
				graph = _window.Graph;
			if (graph != null)
				SerializedGraph = new SerializedObject(graph);
		}

		/// <summary>
		/// Allow you to create your own edge connector listener
		/// </summary>
		/// <returns></returns>
		protected virtual BaseEdgeConnectorListener CreateEdgeConnectorListener()
			=> new(this);

		private void InitializeGraphView()
		{
			graph.onSubgraphParameterListChanged += OnSubgraphParameterListChanged;
			graph.onGraphChanges += GraphChangesCallback;
			if (NodeGraphState.TryGetStateValue(graph, out _viewState, out string guid))
			{
				viewTransform.position = _viewState.Position;
				viewTransform.scale = new Vector3(_viewState.Scale, _viewState.Scale, 1);
			}
			else
			{
				_viewState = new NodeGraphState.StateValue
				{
					Guid = guid,
					Position = Vector3.zero,
					Scale = 1
				};
				schedule.Execute(ResetPositionAndZoom);
			}

			nodeCreationRequest = c => SearchWindow.Open(new SearchWindowContext(c.screenMousePosition), _createNodeMenu);
		}

		private void OnSubgraphParameterListChanged()
		{
			for (int i = graph.nodes.Count - 1; i >= 0; i--)
			{
				BaseNode node = graph.nodes[i];
				if (node is not ParameterNode parameter) continue;
				if (graph.GetSubgraphParameterFromGuid(parameter.parameterGUID) == null)
					RemoveNode(node);
			}

			UpdateSerializedProperties();
			onSubgraphParameterListChanged?.Invoke();
		}

		private void InitializeNodeViews()
		{
			graph.nodes.RemoveAll(n => n == null);

			foreach (BaseNode node in graph.nodes)
			{
				BaseNodeView v = AddNodeView(node);
			}
		}

		private void InitializeEdgeViews()
		{
			// Sanitize edges in case a node broke something while loading
			int removedEdges = graph.edges.RemoveAll(edge => edge == null || edge.ToNode == null || edge.FromNode == null);
			if (removedEdges > 0)
			{
				Debug.LogWarning($"[NodeGraph] {removedEdges} invalid edges were removed from {graph}.", graph);
				EditorUtility.SetDirty(graph);
			}

			foreach (SerializableEdge serializedEdge in graph.edges)
			{
				NodeViewsPerNode.TryGetValue(serializedEdge.ToNode, out BaseNodeView inputNodeView);
				NodeViewsPerNode.TryGetValue(serializedEdge.FromNode, out BaseNodeView outputNodeView);
				if (inputNodeView == null || outputNodeView == null)
				{
					Debug.LogWarning($"[NodeGraph] The node for the edge {serializedEdge} could not be found.", graph);
					continue;
				}

				EdgeView edgeView = new()
				{
					userData = serializedEdge,
					input = inputNodeView.GetPortView(serializedEdge.InputFieldPath, serializedEdge.inputPortIdentifier),
					output = outputNodeView.GetPortView(serializedEdge.OutputFieldPath, serializedEdge.outputPortIdentifier)
				};


				ConnectView(edgeView);
			}

			if (_edgeViews.Count != graph.edges.Count)
			{
				Debug.LogWarning(
					"[NodeGraph] The amount of edges visible in the graph is not the same as the real amount.\n" +
					$"{_edgeViews.Count} views to {graph.edges.Count} edges.",
					graph
				);
			}
		}

		private void InitializeViews()
		{
			foreach (PinnedElement pinnedElement in graph.pinnedElements)
			{
				if (pinnedElement.opened)
					OpenPinned(pinnedElement.editorType.Type);
			}
		}

		private void InitializeGroups()
		{
			foreach (Group group in graph.groups)
			{
				GroupView view = AddGroupView(group);
				view.EnsureMinSize();
			}
		}

		private void InitializeStickyNotes()
		{
#if UNITY_2020_1_OR_NEWER
			foreach (StickyNote group in graph.stickyNotes)
				AddStickyNoteView(group);
#endif
		}

		private void InitializeStackNodes()
		{
			foreach (BaseStackNode stackNode in graph.stackNodes)
				AddStackNodeView(stackNode);
		}

		protected virtual void InitializeManipulators()
		{
			this.AddManipulator(new ContentDragger());
			this.AddManipulator(new SelectionDragger());
			this.AddManipulator(new RectangleSelector());
		}

		protected virtual void Reload()
		{
		}

		#endregion

		#region Graph content modification

		public void UpdateNodeInspectorSelection()
		{
			NodeInspector.previouslySelectedObject = Selection.activeObject;

			var selectedNodeViews = new HashSet<BaseNodeView>();
			NodeInspector.selectedNodes.Clear();
			foreach (ISelectable e in selection)
			{
				if (e is BaseNodeView v && Contains(v) && v.NodeTarget.NeedsInspector)
					selectedNodeViews.Add(v);
			}

			NodeInspector.UpdateSelectedNodes(selectedNodeViews);
			if (Selection.activeObject != NodeInspector && selectedNodeViews.Count > 0)
				Selection.activeObject = NodeInspector;
		}

		public BaseNodeView AddNode(BaseNode node)
		{
			// This will initialize the node using the graph instance
			graph.AddNode(node);

			UpdateSerializedProperties();

			BaseNodeView view = AddNodeView(node);

			// Call create after the node have been initialized
			try
			{
				view.OnCreated();
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}

			return view;
		}

		public BaseNodeView AddNodeView(BaseNode node)
		{
			Type viewType = NodeProvider.GetNodeViewTypeFromType(node.GetType())
			                ?? typeof(BaseNodeView);

			var baseNodeView = (BaseNodeView)Activator.CreateInstance(viewType);
			baseNodeView.Initialize(this, node);
			AddElement(baseNodeView);

			NodeViews.Add(baseNodeView);
			NodeViewsPerNode[node] = baseNodeView;

			return baseNodeView;
		}

		public void RemoveNode(BaseNode node)
		{
			if (NodeViewsPerNode.TryGetValue(node, out BaseNodeView view))
				RemoveNodeView(view);
			graph.RemoveNode(node);
		}

		public void RemoveNodeView(BaseNodeView nodeView)
		{
			RemoveElement(nodeView);
			NodeViews.Remove(nodeView);
			NodeViewsPerNode.Remove(nodeView.NodeTarget);
		}

		private void RemoveNodeViews()
		{
			foreach (BaseNodeView nodeView in NodeViews)
				RemoveElement(nodeView);
			NodeViews.Clear();
			NodeViewsPerNode.Clear();
		}

		private void RemoveStackNodeViews()
		{
			foreach (BaseStackNodeView stackView in _stackNodeViews)
				RemoveElement(stackView);
			_stackNodeViews.Clear();
		}

		private void RemovePinnedElementViews()
		{
			foreach (PinnedElementView pinnedView in _pinnedElements.Values)
			{
				if (Contains(pinnedView))
					Remove(pinnedView);
			}

			_pinnedElements.Clear();
		}

		public GroupView AddGroup(Group group)
		{
			graph.AddGroup(group);
			return AddGroupView(group);
		}

		public GroupView AddGroupView(Group group)
		{
			var groupView = new GroupView();
			AddElement(groupView);
			groupView.Initialize(this, group);
			_groupViews.Add(groupView);
			return groupView;
		}

		public void RemoveGroup(GroupView group)
		{
			RemoveElement(group);
			graph.RemoveGroup(group.Group);
		}

		public BaseStackNodeView AddStackNode(BaseStackNode stackNode)
		{
			graph.AddStackNode(stackNode);
			return AddStackNodeView(stackNode);
		}

		public BaseStackNodeView AddStackNodeView(BaseStackNode stackNode)
		{
			Type viewType = StackNodeViewProvider.GetStackNodeCustomViewType(stackNode.GetType()) ?? typeof(BaseStackNodeView);
			var stackView = Activator.CreateInstance(viewType, stackNode) as BaseStackNodeView;

			AddElement(stackView);
			_stackNodeViews.Add(stackView);

			stackView.Initialize(this);

			return stackView;
		}

		public void RemoveStackNodeView(BaseStackNodeView stackNodeView)
		{
			_stackNodeViews.Remove(stackNodeView);
			RemoveElement(stackNodeView);
		}

#if UNITY_2020_1_OR_NEWER
		public StickyNoteView AddStickyNote(StickyNote note)
		{
			graph.AddStickyNote(note);
			return AddStickyNoteView(note);
		}

		public StickyNoteView AddStickyNoteView(StickyNote note)
		{
			var c = new StickyNoteView();

			c.Initialize(this, note);

			AddElement(c);

			_stickyNoteViews.Add(c);
			return c;
		}

		public void RemoveStickyNoteView(StickyNoteView view)
		{
			_stickyNoteViews.Remove(view);
			RemoveElement(view);
		}

		public void RemoveStickyNotes()
		{
			foreach (StickyNoteView stickyNodeView in _stickyNoteViews)
				RemoveElement(stickyNodeView);
			_stickyNoteViews.Clear();
		}
#endif

		public void AddSelectionsToGroup(GroupView view)
		{
			foreach (ISelectable selectedNode in selection)
			{
				if (selectedNode is not BaseNodeView node) continue;
				view.EncapsulateElement(node);
			}

			view.EnsureMinSize();
		}

		public void RemoveGroups()
		{
			foreach (GroupView groupView in _groupViews)
				RemoveElement(groupView);
			_groupViews.Clear();
		}

		public bool CanConnectEdge(EdgeView e)
		{
			if (e.input == null || e.output == null)
				return false;

			var inputPortView = (PortView)e.input;
			var outputPortView = (PortView)e.output;

			if (inputPortView.node is not BaseNodeView || outputPortView.node is not BaseNodeView)
			{
				Debug.LogError("Connect aborted !");
				return false;
			}
			
			foreach (EdgeView edgeView in inputPortView.GetEdges())
			{
				if (edgeView.output == outputPortView)
				{
					return false;
				}
			}

			return true;
		}

		public bool ConnectView(EdgeView e, bool autoDisconnectInputs = true)
		{
			if (!CanConnectEdge(e))
				return false;

			var inputPortView = (PortView)e.input;
			var outputPortView = (PortView)e.output;
			var inputNodeView = (BaseNodeView)inputPortView.node;
			var outputNodeView = (BaseNodeView)outputPortView.node;

			//If the input port does not support multi-connection, we remove them
			if (autoDisconnectInputs && !inputPortView.Port.AllowMultipleEdges)
			{
				foreach (EdgeView edge in _edgeViews.Where(ev => ev.input == e.input).ToList())
				{
					// TODO: do not disconnect them if the connected port is the same than the old connected
					DisconnectView(edge);
				}
			}

			// same for the output port:
			if (autoDisconnectInputs && !outputPortView.Port.AllowMultipleEdges)
			{
				foreach (EdgeView edge in _edgeViews.Where(ev => ev.output == e.output).ToList())
				{
					// TODO: do not disconnect them if the connected port is the same than the old connected
					DisconnectView(edge);
				}
			}

			AddElement(e);

			if (
				inputNodeView.IsUnmorphedGenericNode(out Type baseTypeConstraint)
				&& inputPortView.PortType == baseTypeConstraint
				&& inputNodeView.MorphNodeToGenericNodeType(outputPortView.PortType, solidifyType: true)
			)
			{
				e.input = null;
				NodeViewsPerNode[inputNodeView.NodeTarget] = inputNodeView;
			}
			else if (
				outputNodeView.IsUnmorphedGenericNode(out baseTypeConstraint)
				&& outputPortView.PortType == baseTypeConstraint
				&& outputNodeView.MorphNodeToGenericNodeType(inputPortView.PortType, solidifyType: true)
			)
			{
				e.output = null;
				NodeViewsPerNode[outputNodeView.NodeTarget] = outputNodeView;
			}

			// If the input port have been removed by the custom port behavior
			// we try to find if it's still here
			e.input ??= inputNodeView.GetPortView(inputPortView.FieldPath, inputPortView.Port.Identifier);
			e.output ??= outputNodeView.GetPortView(outputPortView.FieldPath, outputPortView.Port.Identifier);
			
			e.input.Connect(e);
			e.output.Connect(e);

			_edgeViews.Add(e);

			inputNodeView.RefreshPorts();
			outputNodeView.RefreshPorts();

			// In certain cases the edge color is wrong so we patch it
			schedule.Execute(() => { e.UpdateEdgeControl(); }).ExecuteLater(1);

			e.IsConnected = true;

			return true;
		}

		public bool Connect(PortView fromPortView, PortView toPortView, bool autoDisconnectInputs = true)
		{
			NodePort toPort = toPortView.Owner.NodeTarget.GetPort(toPortView.FieldPath, toPortView.Port.Identifier);
			NodePort fromPort = fromPortView.Owner.NodeTarget.GetPort(fromPortView.FieldPath, fromPortView.Port.Identifier);

			// Checks that the node we are connecting still exists
			if (toPortView.Owner.parent == null || fromPortView.Owner.parent == null)
				return false;

			var newEdge = SerializableEdge.CreateNewEdge(graph, fromPort, toPort);

			EdgeView edgeView = new()
			{
				userData = newEdge,
				output = fromPortView,
				input = toPortView
			};
			return Connect(edgeView);
		}

		public bool Connect(EdgeView e, bool autoDisconnectInputs = true)
		{
			if (!CanConnectEdge(e))
				return false;

			var inputPortView = (PortView)e.input;
			var outputPortView = (PortView)e.output;
			var inputNodeView = (BaseNodeView)inputPortView.node;
			var outputNodeView = (BaseNodeView)outputPortView.node;
			NodePort inputPort = inputNodeView.NodeTarget.GetPort(inputPortView.FieldPath, inputPortView.Port.Identifier);
			NodePort outputPort = outputNodeView.NodeTarget.GetPort(outputPortView.FieldPath, outputPortView.Port.Identifier);

			e.userData = graph.Connect(outputPort, inputPort, autoDisconnectInputs);

			ConnectView(e, autoDisconnectInputs);
			return true;
		}

		public void DisconnectView(EdgeView e, bool refreshPorts = true)
		{
			if (e == null)
				return;

			RemoveElement(e);

			if (e.input?.node is BaseNodeView inputNodeView)
			{
				e.input.Disconnect(e);
				if (refreshPorts)
					inputNodeView.RefreshPorts();
			}

			if (e.output?.node is BaseNodeView outputNodeView)
			{
				e.output.Disconnect(e);
				if (refreshPorts)
					outputNodeView.RefreshPorts();
			}

			_edgeViews.Remove(e);
		}

		public void Disconnect(EdgeView e, bool refreshPorts = true)
		{
			// Remove the serialized edge if there is one
			if (e.userData is SerializableEdge serializableEdge)
				graph.Disconnect(serializableEdge.GUID);

			DisconnectView(e, refreshPorts);
		}

		public void RemoveEdges()
		{
			foreach (EdgeView edge in _edgeViews)
				RemoveElement(edge);
			_edgeViews.Clear();
		}

		public void RegisterCompleteObjectUndo(string name) => Undo.RegisterCompleteObjectUndo(graph, name);

		public void SaveGraphToDisk()
		{
			if (graph == null)
				return;

			EditorUtility.SetDirty(graph);
			NodeGraphState.SaveToDisk();
		}

		public bool ToggleView<T>() where T : PinnedElementView => ToggleView(typeof(T));

		public bool ToggleView(Type type)
		{
			PinnedElementView view;
			_pinnedElements.TryGetValue(type, out view);

			if (view == null)
			{
				OpenPinned(type);
				return true;
			}

			ClosePinned(type, view);
			return false;
		}

		public void OpenPinned<T>() where T : PinnedElementView => OpenPinned(typeof(T));

		public void OpenPinned(Type type)
		{
			PinnedElementView view;

			if (type == null)
				return;

			PinnedElement elem = graph.OpenPinned(type);

			if (!_pinnedElements.ContainsKey(type))
			{
				view = Activator.CreateInstance(type) as PinnedElementView;
				if (view == null)
					return;
				_pinnedElements[type] = view;
				view.InitializeGraphView(elem, this);
			}

			view = _pinnedElements[type];

			if (!Contains(view))
				Add(view);
		}

		public void ClosePinned<T>(PinnedElementView view) where T : PinnedElementView => ClosePinned(typeof(T), view);

		public void ClosePinned(Type type, PinnedElementView elem)
		{
			_pinnedElements.Remove(type);
			elem.RemoveFromHierarchy();
			graph.ClosePinned(type);
		}

		public Status GetPinnedElementStatus<T>() where T : PinnedElementView => GetPinnedElementStatus(typeof(T));

		public Status GetPinnedElementStatus(Type type)
		{
			if (graph == null)
				return Status.Hidden;

			PinnedElement pinned = graph.pinnedElements.Find(p => p.editorType.Type == type);
			return pinned is { opened: true } ? Status.Normal : Status.Hidden;
		}

		public void ResetPositionAndZoom()
		{
			Vector2 min = graph.nodes.Aggregate(Vector2.zero, (current, node) => new Vector2(Mathf.Min(current.x, node.position.x), Mathf.Min(current.y, node.position.y)));
			Vector2 max = graph.nodes.Aggregate(Vector2.zero, (current, node) => new Vector2(Mathf.Max(current.x, node.position.x), Mathf.Max(current.y, node.position.y)));
			max += new Vector2(100f, 200f); // Expand by a normal size for a node.
			Vector2 position = (min + max) * 0.5f;

			UpdateViewTransform(-position + localBound.size * 0.5f, Vector3.one);
		}

		/// <summary>
		/// Deletes the selected content, can be called form an IMGUI container
		/// </summary>
		public void DelayedDeleteSelection() => schedule.Execute(() => DeleteSelectionOperation("Delete", AskUser.DontAskUser)).ExecuteLater(0);

		protected virtual void InitializeView()
		{
		}

		public virtual IEnumerable<(string path, Type type, ConfigureNode configuration)> FilterCreateNodeMenuEntries()
		{
			// By default we don't filter anything
			foreach ((string path, Type type, ConfigureNode configuration) nodeMenuItem in NodeProvider.GetNodeMenuEntries(graph))
				yield return nodeMenuItem;

			// TODO: add exposed properties to this list
		}

		public SimplifiedRelayNodeView AddRelayNode(PortView inputPort, PortView outputPort, Vector2 position)
		{
			var relayNode = BaseNode.CreateFromType<SimplifiedRelayNode>(position);
			var view = (SimplifiedRelayNodeView)AddNode(relayNode);

			if (outputPort != null)
				Connect(outputPort, view.InputPortViews[0]);
			if (inputPort != null)
				Connect(view.OutputPortViews[0], inputPort);

			return view;
		}

		/// <summary>
		/// Update all the serialized property bindings (in case a node was deleted / added, the property pathes needs to be updated)
		/// </summary>
		private void SyncSerializedPropertyPaths()
		{
			foreach (BaseNodeView nodeView in NodeViews)
				nodeView.SyncSerializedPropertyPaths();
			NodeInspector.RefreshNodes();
		}

		/// <summary>
		/// Call this function when you want to remove this view
		/// </summary>
		public void Dispose()
		{
			ClearGraphElements();
			RemoveFromHierarchy();
			Undo.undoRedoPerformed -= ReloadView;
			Object.DestroyImmediate(NodeInspector);

			graph.onSubgraphParameterListChanged -= OnSubgraphParameterListChanged;
			graph.onGraphChanges -= GraphChangesCallback;
		}

		#endregion

		/// <summary>
		/// Opens a graph as a subgraph, appending the currently opened graph to the breadcrumbs.
		/// </summary>
		public void OpenSubgraph(BaseGraph subgraph) => _window.OpenSubgraph(subgraph);

		private void CreateSubgraph()
		{
			HashSet<BaseNodeView> viewsInSubgraph = selection.OfType<BaseNodeView>().ToHashSet();
			List<GroupView> groupsInSubgraph = selection.OfType<GroupView>().ToList();
			HashSet<BaseNode> inSubgraph = viewsInSubgraph.Select(v => v.NodeTarget).ToHashSet();

			string assetPath = AssetDatabase.GetAssetPath(graph);
			string directory = Path.GetDirectoryName(assetPath)!;
			string subgraphPath = EditorUtility.SaveFilePanelInProject(
				"Create Subgraph",
				"New Subgraph",
				"asset",
				$"Creating a subgraph out of {inSubgraph.Count} nodes.",
				directory
			);

			if (string.IsNullOrEmpty(subgraphPath))
				return;

			subgraphPath = AssetDatabase.GenerateUniqueAssetPath(subgraphPath);

			// Gather the edges that make up the border of the subgraph.
			Dictionary<(PortView port, bool isInput), List<EdgeView>> borderEdges = new();
			foreach (BaseNodeView node in viewsInSubgraph)
			{
				foreach (PortView port in node.AllPortViews)
				{
					foreach (EdgeView edgeView in port.GetEdges())
					{
						SerializableEdge edge = edgeView.SerializedEdge;
						if (!inSubgraph.Contains(edge.FromNode))
						{
							var key = (port, true);
							if (!borderEdges.TryGetValue(key, out var list))
								borderEdges.Add(key, list = new());
							list.Add(edgeView);
							RemoveFromSelection(edgeView);
							continue;
						}

						if (!inSubgraph.Contains(edge.ToNode))
						{
							var key = (port, false);
							if (!borderEdges.TryGetValue(key, out var list))
								borderEdges.Add(key, list = new());
							list.Add(edgeView);
							RemoveFromSelection(edgeView);
							continue;
						}

						// Add any edges that are entirely within the subgraph to our selection.
						AddToSelection(edgeView);
					}
				}
			}

			// Copy the current selection (i.e. the subgraph nodes and edges).
			HashSet<GraphElement> graphElementSet = new();
			CollectCopyableGraphElements(selection.OfType<GraphElement>(), graphElementSet);
			string copy = SerializeGraphElements(graphElementSet);
			if (string.IsNullOrEmpty(copy))
			{
				Debug.LogWarning("Copy of elements was empty, subgraph creation was cancelled.");
				return;
			}


			Vector2 center = inSubgraph.Aggregate(Vector2.zero, (vector2, node) => vector2 + node.position) / inSubgraph.Count;

			// Get the most specific graph required by the nodes.
			Type thisType = graph.GetType();
			Type graphType = thisType;
			while (!graphType.BaseType?.IsAbstract ?? false)
				graphType = graphType.BaseType; // the default graph type is the highest non-abstract superclass
			HashSet<Type> nodeTypes = viewsInSubgraph.Select(v => v.NodeTarget.GetType()).ToHashSet();
			foreach (Type nodeType in nodeTypes)
			{
				// Get the least specific graph requirement from a node.
				Type requirement = null;
				foreach (Type type in NodeProvider.GetGraphTypeRequirementFromType(nodeType))
				{
					requirement = GetLeastSpecific(requirement, type);
					if (thisType == requirement)
						goto Create;
					if (!thisType.IsSubclassOf(requirement))
						requirement = null; // Ignore any types which are not superclasses of this graph.
				}

				graphType = GetMostSpecific(requirement, graphType);

				continue;

				Type GetLeastSpecific([CanBeNull] Type a, Type b)
				{
					if (a == null) return b;
					if (a == b) return a;
					return a.IsSubclassOf(b) ? b : a;
				}

				Type GetMostSpecific([CanBeNull] Type a, Type b)
				{
					if (a == null) return b;
					if (a == b) return a;
					return a.IsSubclassOf(b) ? a : b;
				}
			}

			Create:
			
			// Create the subgraph asset.
			var subgraph = (BaseGraph)ScriptableObject.CreateInstance(graphType);
			var subgraphNodeView = new BaseGraphView(_window); // We create a view so we can paste into it.
			subgraphNodeView.Initialize(subgraph);

			// Paste the subgraph into the asset.
			subgraphNodeView.UnserializeAndPasteOperation(CreateSubgraphKey, copy);

			Dictionary<PortView, string> parameterLookup = new();
			Dictionary<PortView, Vector2> positionLookup = new();

			// Cache the positions of the ports so we can sort the parameters by coordinate.
			foreach ((PortView port, _) in borderEdges.Keys)
			{
				positionLookup.Add(port, port.ChangeCoordinatesTo(contentViewContainer, Vector2.zero));
			}

			// For every port connected to a border edge.
			// Hook-up edges to parameter nodes.
			foreach (
				((PortView port, bool isInputParameter), var list) in borderEdges
					// Sort parameters by coordinate.
					.OrderBy(kvp => positionLookup[kvp.Key.port].x)
					.ThenBy(kvp => positionLookup[kvp.Key.port].y)
			)
			{
				// Create a matching parameter.
				string parameterGuid = subgraph.AddSubgraphParameter(port.Port.EditorDisplayName, port.PortType, isInputParameter ? ParameterDirection.Input : ParameterDirection.Output);

				parameterLookup.Add(port, parameterGuid);

				// For each border edge.
				foreach (EdgeView edge in list)
				{
					// Create a parameter node using the parameter guid.
					var source = (PortView)(isInputParameter ? edge.output : edge.input);
					var parameterNode = BaseNode.CreateFromType<ParameterNode>(source.ChangeCoordinatesTo(contentViewContainer, Vector2.zero));
					parameterNode.parameterGUID = parameterGuid;
					subgraph.AddNode(parameterNode);

					// Find the nodes to connect edges to.
					BaseNode copiedNode = subgraphNodeView._lastCopiedNodesMap[port.Owner.NodeTarget.GUID];
					NodePort originPortInSubgraph = copiedNode.GetPort(port.FieldPath, port.Port.Identifier);

					// Connect the parameter nodes to their matching ports.
					if (isInputParameter)
					{
						NodePort from = parameterNode.OutputPorts.First();
						subgraph.Connect(from, originPortInSubgraph);
					}
					else
					{
						NodePort to = parameterNode.InputPorts.First();
						subgraph.Connect(originPortInSubgraph, to);
					}
				}
			}

			subgraph.name = Path.GetFileNameWithoutExtension(subgraphPath);
			AssetDatabase.CreateAsset(subgraph, subgraphPath);
			
			Undo.RegisterCompleteObjectUndo(graph, "Create Subgraph");
			var subgraphNode = BaseNode.CreateFromType<SubgraphNode>(center);
			subgraphNode.Subgraph = subgraph;
			BaseNodeView view = AddNode(subgraphNode);

			// Hookup border edges to the subgraph node's ports.
			foreach (((PortView port, bool isInputParameter), var list) in borderEdges)
			{
				string parameter = parameterLookup[port];
				if (isInputParameter)
				{
					PortView to = view.GetPortView(
						SubgraphNode.InputPortKey,
						parameter
					);
					foreach (EdgeView edge in list)
					{
						Connect((PortView)edge.output, to);
					}
				}
				else
				{
					PortView from = view.GetPortView(
						SubgraphNode.OutputPortKey,
						parameter
					);
					foreach (EdgeView edge in list)
					{
						Connect(from, (PortView)edge.input);
					}
				}
			}

			// Delete and disconnect all the nodes that have become a part of the subgraph.
			foreach (BaseNode node in inSubgraph)
				RemoveNode(node);

			foreach (GroupView groupView in groupsInSubgraph)
				RemoveGroup(groupView);
		}

		private void UnpackSubgraph()
		{
			Undo.RegisterCompleteObjectUndo(graph, "Unpack Subgraph");
			var subgraphNode = (SubgraphNode)selection.OfType<SubgraphNodeView>().First().NodeTarget;
			graph.InlineSubgraphNode(subgraphNode);
			graph.RemoveNode(subgraphNode);
			Initialize(graph); // Reload this completely.
		}
	}
}
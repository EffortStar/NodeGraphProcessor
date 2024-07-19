using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.Experimental.GraphView;
using System.Linq;
using System;
using UnityEditor.SceneManagement;
using System.Reflection;
using JetBrains.Annotations;
using Status = UnityEngine.UIElements.DropdownMenuAction.Status;
using Object = UnityEngine.Object;

namespace GraphProcessor
{
	/// <summary>
	/// Base class to write a custom view for a node
	/// </summary>
	public class BaseGraphView : GraphView, IDisposable
	{
		/// <summary>
		/// Graph that owns of the node
		/// </summary>
		public BaseGraph graph;

		/// <summary>
		/// Connector listener that will create the edges between ports
		/// </summary>
		public BaseEdgeConnectorListener connectorListener;

		/// <summary>
		/// List of all node views in the graph
		/// </summary>
		/// <typeparam name="BaseNodeView"></typeparam>
		/// <returns></returns>
		public List<BaseNodeView> nodeViews = new();

		/// <summary>
		/// Dictionary of the node views accessed view the node instance, faster than a Find in the node view list
		/// </summary>
		/// <typeparam name="BaseNode"></typeparam>
		/// <typeparam name="BaseNodeView"></typeparam>
		/// <returns></returns>
		public readonly Dictionary<BaseNode, BaseNodeView> nodeViewsPerNode = new();

		/// <summary>
		/// List of all edge views in the graph
		/// </summary>
		/// <typeparam name="EdgeView"></typeparam>
		/// <returns></returns>
		public readonly List<EdgeView> edgeViews = new();

		/// <summary>
		/// List of all group views in the graph
		/// </summary>
		/// <typeparam name="GroupView"></typeparam>
		/// <returns></returns>
		public readonly List<GroupView> groupViews = new();

#if UNITY_2020_1_OR_NEWER
		/// <summary>
		/// List of all sticky note views in the graph
		/// </summary>
		/// <typeparam name="StickyNoteView"></typeparam>
		/// <returns></returns>
		public readonly List<StickyNoteView> stickyNoteViews = new();
#endif

		/// <summary>
		/// List of all stack node views in the graph
		/// </summary>
		/// <typeparam name="BaseStackNodeView"></typeparam>
		/// <returns></returns>
		public readonly List<BaseStackNodeView> stackNodeViews = new();

		private readonly Dictionary<Type, PinnedElementView> pinnedElements = new();

		private readonly CreateNodeMenuWindow createNodeMenu;

		/// <summary>
		/// Triggered just after the graph is initialized
		/// </summary>
		public event Action initialized;

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
		[SerializeField]
		protected NodeInspectorObject nodeInspector
		{
			get
			{
				if (graph.nodeInspectorReference == null)
					graph.nodeInspectorReference = CreateNodeInspectorObject();
				return graph.nodeInspectorReference as NodeInspectorObject;
			}
		}

		public SerializedObject serializedGraph { get; private set; }

		private Dictionary<Type, (Type nodeType, MethodInfo initalizeNodeFromObject)> nodeTypePerCreateAssetType = new();
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

			createNodeMenu = ScriptableObject.CreateInstance<CreateNodeMenuWindow>();
			createNodeMenu.Initialize(this, window);
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

			foreach (BaseNodeView nodeView in elements.Where(e => e is BaseNodeView))
			{
				data.copiedNodes.Add(JsonSerializer.SerializeNode(nodeView.nodeTarget));
				foreach (NodePort port in nodeView.nodeTarget.AllPorts)
				{
					if (port.portData.vertical)
					{
						foreach (SerializableEdge edge in port.GetEdges())
							data.copiedEdges.Add(JsonSerializer.Serialize(edge));
					}
				}
			}

			foreach (GroupView groupView in elements.Where(e => e is GroupView))
				data.copiedGroups.Add(JsonSerializer.Serialize(groupView.Group));

			foreach (EdgeView edgeView in elements.Where(e => e is EdgeView))
				data.copiedEdges.Add(JsonSerializer.Serialize(edgeView.serializedEdge));
			
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
			ClearSelection();
			
			RegisterCompleteObjectUndo(operationName);

			var data = JsonUtility.FromJson<CopyPasteHelper>(serializedData);

			Dictionary<string, BaseNode> copiedNodesMap = _lastCopiedNodesMap = new Dictionary<string, BaseNode>();

			List<Group> unserializedGroups = data.copiedGroups.Select(g => JsonSerializer.Deserialize<Group>(g)).ToList();

			foreach (JsonElement serializedNode in data.copiedNodes)
			{
				BaseNode node = JsonSerializer.DeserializeNode(serializedNode);

				if (node == null)
					continue;

				string sourceGUID = node.GUID;
				graph.nodesPerGUID.TryGetValue(sourceGUID, out BaseNode sourceNode);
				//Call OnNodeCreated on the new fresh copied node
				node.createdFromDuplication = true;
				node.OnNodeCreated();
				//And move a bit the new node
				node.position += new Vector2(20, 20);

				AddNode(node);

				copiedNodesMap[sourceGUID] = node;

				// Select the new node
				AddToSelection(nodeViewsPerNode[node]);
			}

			foreach (Group group in unserializedGroups)
			{
				// Same than for node
				AddGroup(group);
			}

			foreach (JsonElement serializedEdge in data.copiedEdges)
			{
				var edge = JsonSerializer.Deserialize<SerializableEdge>(serializedEdge);
				
				edge.Deserialize(false);

				var retry = false;
				if (edge.ToNode == null)
				{
					if (!copiedNodesMap.TryGetValue(edge.ToNodeGuid, out BaseNode node))
						continue;
					edge.ToNode = node;
					retry = true;
				}
				
				if (edge.FromNode == null)
				{
					if (!copiedNodesMap.TryGetValue(edge.FromNodeGuid, out BaseNode node))
						continue;
					edge.FromNode = node;
					retry = true;
				}
				
				if (retry)
				{
					edge.OnBeforeSerialize();
					edge.Deserialize();
				}
				
				if (edge.ToNode == null || edge.FromNode == null)
				{
					continue;
				}

				// Find port of new nodes:
				copiedNodesMap.TryGetValue(edge.ToNode.GUID, out BaseNode oldInputNode);
				copiedNodesMap.TryGetValue(edge.FromNode.GUID, out BaseNode oldOutputNode);

				// We avoid to break the graph by replacing unique connections:
				if (oldInputNode == null && !edge.ToPort.portData.acceptMultipleEdges || !edge.FromPort.portData.acceptMultipleEdges)
					continue;

				oldInputNode ??= edge.ToNode;
				oldOutputNode ??= edge.FromNode;

				NodePort inputPort = oldInputNode.GetPort(edge.ToPort.fieldName, edge.inputPortIdentifier);
				NodePort outputPort = oldOutputNode.GetPort(edge.FromPort.fieldName, edge.outputPortIdentifier);

				var newEdge = SerializableEdge.CreateNewEdge(graph, outputPort, inputPort);

				if (nodeViewsPerNode.ContainsKey(oldInputNode) && nodeViewsPerNode.ContainsKey(oldOutputNode))
				{
					EdgeView edgeView = CreateEdgeView();
					edgeView.userData = newEdge;
					edgeView.input = nodeViewsPerNode[oldInputNode].GetPortViewFromFieldName(newEdge.inputFieldName, newEdge.inputPortIdentifier);
					edgeView.output = nodeViewsPerNode[oldOutputNode].GetPortViewFromFieldName(newEdge.outputFieldName, newEdge.outputPortIdentifier);

					Connect(edgeView);
				}
			}
			
			contentViewContainer.AddManipulator(new PostPasteNodesManipulator(this, copiedNodesMap));
		}

		public virtual EdgeView CreateEdgeView()
		{
			return new EdgeView();
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
							foreach (PortView pv in nodeView.inputPortViews.Concat(nodeView.outputPortViews))
								if (pv.orientation == Orientation.Vertical)
									foreach (EdgeView edge in pv.GetEdges().ToList())
										Disconnect(edge);

							nodeInspector.NodeViewRemoved(nodeView);
							ExceptionToLog.Call(() => nodeView.OnRemoved());
							graph.RemoveNode(nodeView.nodeTarget);
							UpdateSerializedProperties();
							RemoveElement(nodeView);
							if (Selection.activeObject == nodeInspector)
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
				EdgeView edge = edgeViews.FirstOrDefault(e => e.serializedEdge == changes.removedEdge);

				DisconnectView(edge);

				RemoveRelayIfRequiredAfterDelay(changes.removedEdge.FromNode);
				RemoveRelayIfRequiredAfterDelay(changes.removedEdge.ToNode);
			}

			if (changes.removedGroups != null)
			{
				GroupView view = groupViews.FirstOrDefault(g => g.Group == changes.removedGroups);
				if (view != null)
				{
					RemoveElement(view);
					groupViews.Remove(view);
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
				if (!nodeViewsPerNode.ContainsKey(relay))
					return;

				if (
					relay.inputPorts[0].GetEdges().Count != 0 ||
					relay.outputPorts[0].GetEdges().Count != 0
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

				if (portView.owner == ((PortView)startPort).owner)
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
				foreach (KeyValuePair<Type, PinnedElementView> kp in pinnedElements)
					kp.Value.ResetPosition();
			});
		}

		protected virtual void KeyDownCallback(KeyDownEvent e)
		{
			if (e.keyCode == KeyCode.LeftControl)
				return;
			
			if (e.keyCode == KeyCode.S && (e.commandKey || e.ctrlKey))
			{
				SaveGraphToDisk();
				e.StopPropagation();
			}
			if (e.keyCode == KeyCode.G && (e.commandKey || e.ctrlKey))
			{
				AddSelectionsToGroup(AddGroup(new Group("New Group")));
				e.StopPropagation();
			}
			else if (nodeViews.Count > 0 && (e.commandKey || e.ctrlKey) && e.altKey)
			{
				//	Node Aligning shortcuts
				switch (e.keyCode)
				{
					case KeyCode.LeftArrow:
						nodeViews[0].AlignToLeft();
						e.StopPropagation();
						break;
					case KeyCode.RightArrow:
						nodeViews[0].AlignToRight();
						e.StopPropagation();
						break;
					case KeyCode.UpArrow:
						nodeViews[0].AlignToTop();
						e.StopPropagation();
						break;
					case KeyCode.DownArrow:
						nodeViews[0].AlignToBottom();
						e.StopPropagation();
						break;
					case KeyCode.C:
						nodeViews[0].AlignToCenter();
						e.StopPropagation();
						break;
					case KeyCode.M:
						nodeViews[0].AlignToMiddle();
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
				nodeViews.ForEach(v => v.CloseSettings());
			}

			if (DoesSelectionContainsInspectorNodes())
				UpdateNodeInspectorSelection();
		}

		private bool DoesSelectionContainsInspectorNodes()
		{
			List<ISelectable> selectedNodes = selection.Where(s => s is BaseNodeView).ToList();
			List<ISelectable> selectedNodesNotInInspector = selectedNodes.Except(nodeInspector.selectedNodes).ToList();
			List<ISelectable> nodeInInspectorWithoutSelectedNodes = nodeInspector.selectedNodes.Except(selectedNodes).ToList();

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
						break;
					}

					Type objectType = obj.GetType();

					foreach (KeyValuePair<Type, (Type nodeType, MethodInfo initalizeNodeFromObject)> kp in nodeTypePerCreateAssetType)
					{
						if (!kp.Key.IsAssignableFrom(objectType))
							continue;
						try
						{
							var node = BaseNode.CreateFromType(kp.Value.nodeType, mousePos);
							if ((bool)kp.Value.initalizeNodeFromObject.Invoke(node, new[] { obj }))
							{
								AddNode(node);
								break;
							}
						}
						catch (Exception exception)
						{
							Debug.LogException(exception);
						}
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
					selectedNodeGUIDs.Add(v.nodeTarget.GUID);
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
				AddToSelection(nodeViews.FirstOrDefault(n => n.nodeTarget.GUID == guid));
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

			connectorListener = CreateEdgeConnectorListener();

			// When pressing ctrl-s, we save the graph
			EditorSceneManager.sceneSaved += _ => SaveGraphToDisk();
			RegisterCallback<KeyDownEvent>(e =>
			{
				if (e.keyCode == KeyCode.S && e.actionKey)
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

			initialized?.Invoke();

			InitializeView();

			// Register the nodes that can be created from assets
			foreach ((string path, Type type) nodeInfo in NodeProvider.GetNodeMenuEntries(graph))
			{
				Type[] interfaces = nodeInfo.type.GetInterfaces();
				IEnumerable<Type> exceptInheritedInterfaces = interfaces.Except(interfaces.SelectMany(t => t.GetInterfaces()));
				foreach (Type i in interfaces)
				{
					if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICreateNodeFrom<>))
					{
						Type genericArgumentType = i.GetGenericArguments()[0];
						MethodInfo initializeFunction = nodeInfo.type.GetMethod(
							nameof(ICreateNodeFrom<Object>.InitializeNodeFromObject),
							BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
							null, new Type[] { genericArgumentType }, null
						);

						// We only add the type that implements the interface, not it's children
						if (initializeFunction.DeclaringType == nodeInfo.type)
							nodeTypePerCreateAssetType[genericArgumentType] = (nodeInfo.type, initializeFunction);
					}
				}
			}
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
				serializedGraph = new SerializedObject(graph);
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

			nodeCreationRequest = c => SearchWindow.Open(new SearchWindowContext(c.screenMousePosition), createNodeMenu);
		}

		private void OnSubgraphParameterListChanged()
		{
			for (int i = graph.nodes.Count - 1; i >= 0; i--)
			{
				BaseNode node = graph.nodes[i];
				if (node is not ParameterNode parameter) continue;
				if (graph.GetSubgraphParameterFromGUID(parameter.parameterGUID) == null)
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
			graph.edges.RemoveAll(edge => edge == null || edge.ToNode == null || edge.FromNode == null);

			foreach (SerializableEdge serializedEdge in graph.edges)
			{
				nodeViewsPerNode.TryGetValue(serializedEdge.ToNode, out BaseNodeView inputNodeView);
				nodeViewsPerNode.TryGetValue(serializedEdge.FromNode, out BaseNodeView outputNodeView);
				if (inputNodeView == null || outputNodeView == null)
					continue;

				EdgeView edgeView = CreateEdgeView();
				edgeView.userData = serializedEdge;
				edgeView.input = inputNodeView.GetPortViewFromFieldName(serializedEdge.inputFieldName, serializedEdge.inputPortIdentifier);
				edgeView.output = outputNodeView.GetPortViewFromFieldName(serializedEdge.outputFieldName, serializedEdge.outputPortIdentifier);


				ConnectView(edgeView);
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
				AddGroupView(group);
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
			nodeInspector.previouslySelectedObject = Selection.activeObject;

			var selectedNodeViews = new HashSet<BaseNodeView>();
			nodeInspector.selectedNodes.Clear();
			foreach (ISelectable e in selection)
			{
				if (e is BaseNodeView v && Contains(v) && v.nodeTarget.needsInspector)
					selectedNodeViews.Add(v);
			}

			nodeInspector.UpdateSelectedNodes(selectedNodeViews);
			if (Selection.activeObject != nodeInspector && selectedNodeViews.Count > 0)
				Selection.activeObject = nodeInspector;
		}

		public BaseNodeView AddNode(BaseNode node)
		{
			// This will initialize the node using the graph instance
			graph.AddNode(node);

			UpdateSerializedProperties();

			BaseNodeView view = AddNodeView(node);

			// Call create after the node have been initialized
			ExceptionToLog.Call(() => view.OnCreated());
			return view;
		}

		public BaseNodeView AddNodeView(BaseNode node)
		{
			Type viewType = NodeProvider.GetNodeViewTypeFromType(node.GetType())
			                ?? typeof(BaseNodeView);

			var baseNodeView = (BaseNodeView)Activator.CreateInstance(viewType);
			baseNodeView.Initialize(this, node);
			AddElement(baseNodeView);

			nodeViews.Add(baseNodeView);
			nodeViewsPerNode[node] = baseNodeView;

			return baseNodeView;
		}

		public void RemoveNode(BaseNode node)
		{
			if (nodeViewsPerNode.TryGetValue(node, out BaseNodeView view))
				RemoveNodeView(view);
			graph.RemoveNode(node);
		}

		public void RemoveNodeView(BaseNodeView nodeView)
		{
			RemoveElement(nodeView);
			nodeViews.Remove(nodeView);
			nodeViewsPerNode.Remove(nodeView.nodeTarget);
		}

		private void RemoveNodeViews()
		{
			foreach (BaseNodeView nodeView in nodeViews)
				RemoveElement(nodeView);
			nodeViews.Clear();
			nodeViewsPerNode.Clear();
		}

		private void RemoveStackNodeViews()
		{
			foreach (BaseStackNodeView stackView in stackNodeViews)
				RemoveElement(stackView);
			stackNodeViews.Clear();
		}

		private void RemovePinnedElementViews()
		{
			foreach (PinnedElementView pinnedView in pinnedElements.Values)
			{
				if (Contains(pinnedView))
					Remove(pinnedView);
			}

			pinnedElements.Clear();
		}

		public GroupView AddGroup(Group block)
		{
			graph.AddGroup(block);
			return AddGroupView(block);
		}

		public GroupView AddGroupView(Group block)
		{
			var c = new GroupView();
			AddElement(c);
			c.Initialize(this, block);

			groupViews.Add(c);
			return c;
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
			stackNodeViews.Add(stackView);

			stackView.Initialize(this);

			return stackView;
		}

		public void RemoveStackNodeView(BaseStackNodeView stackNodeView)
		{
			stackNodeViews.Remove(stackNodeView);
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

			stickyNoteViews.Add(c);
			return c;
		}

		public void RemoveStickyNoteView(StickyNoteView view)
		{
			stickyNoteViews.Remove(view);
			RemoveElement(view);
		}

		public void RemoveStickyNotes()
		{
			foreach (StickyNoteView stickyNodeView in stickyNoteViews)
				RemoveElement(stickyNodeView);
			stickyNoteViews.Clear();
		}
#endif

		public void AddSelectionsToGroup(GroupView view)
		{
			foreach (ISelectable selectedNode in selection)
			{
				if (selectedNode is not GraphElement node) continue;
				view.EncapsulateElement(node);
			}

			view.EnsureMinSize();
		}

		public void RemoveGroups()
		{
			foreach (GroupView groupView in groupViews)
				RemoveElement(groupView);
			groupViews.Clear();
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
			if (autoDisconnectInputs && !inputPortView.portData.acceptMultipleEdges)
			{
				foreach (EdgeView edge in edgeViews.Where(ev => ev.input == e.input).ToList())
				{
					// TODO: do not disconnect them if the connected port is the same than the old connected
					DisconnectView(edge);
				}
			}

			// same for the output port:
			if (autoDisconnectInputs && !outputPortView.portData.acceptMultipleEdges)
			{
				foreach (EdgeView edge in edgeViews.Where(ev => ev.output == e.output).ToList())
				{
					// TODO: do not disconnect them if the connected port is the same than the old connected
					DisconnectView(edge);
				}
			}

			AddElement(e);

			e.input.Connect(e);
			e.output.Connect(e);

			// If the input port have been removed by the custom port behavior
			// we try to find if it's still here
			e.input ??= inputNodeView.GetPortViewFromFieldName(inputPortView.fieldName, inputPortView.portData.identifier);
			e.output ??= inputNodeView.GetPortViewFromFieldName(outputPortView.fieldName, outputPortView.portData.identifier);

			edgeViews.Add(e);

			inputNodeView.RefreshPorts();
			outputNodeView.RefreshPorts();

			// In certain cases the edge color is wrong so we patch it
			schedule.Execute(() => { e.UpdateEdgeControl(); }).ExecuteLater(1);

			e.isConnected = true;

			return true;
		}

		public bool Connect(PortView fromPortView, PortView toPortView, bool autoDisconnectInputs = true)
		{
			NodePort toPort = toPortView.owner.nodeTarget.GetPort(toPortView.fieldName, toPortView.portData.identifier);
			NodePort fromPort = fromPortView.owner.nodeTarget.GetPort(fromPortView.fieldName, fromPortView.portData.identifier);

			// Checks that the node we are connecting still exists
			if (toPortView.owner.parent == null || fromPortView.owner.parent == null)
				return false;

			var newEdge = SerializableEdge.CreateNewEdge(graph, fromPort, toPort);

			EdgeView edgeView = CreateEdgeView();
			edgeView.userData = newEdge;
			edgeView.output = fromPortView;
			edgeView.input = toPortView;
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
			NodePort inputPort = inputNodeView.nodeTarget.GetPort(inputPortView.fieldName, inputPortView.portData.identifier);
			NodePort outputPort = outputNodeView.nodeTarget.GetPort(outputPortView.fieldName, outputPortView.portData.identifier);

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

			edgeViews.Remove(e);
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
			foreach (EdgeView edge in edgeViews)
				RemoveElement(edge);
			edgeViews.Clear();
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
			pinnedElements.TryGetValue(type, out view);

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

			if (!pinnedElements.ContainsKey(type))
			{
				view = Activator.CreateInstance(type) as PinnedElementView;
				if (view == null)
					return;
				pinnedElements[type] = view;
				view.InitializeGraphView(elem, this);
			}

			view = pinnedElements[type];

			if (!Contains(view))
				Add(view);
		}

		public void ClosePinned<T>(PinnedElementView view) where T : PinnedElementView => ClosePinned(typeof(T), view);

		public void ClosePinned(Type type, PinnedElementView elem)
		{
			pinnedElements.Remove(type);
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

		public virtual IEnumerable<(string path, Type type)> FilterCreateNodeMenuEntries()
		{
			// By default we don't filter anything
			foreach ((string path, Type type) nodeMenuItem in NodeProvider.GetNodeMenuEntries(graph))
				yield return nodeMenuItem;

			// TODO: add exposed properties to this list
		}

		public SimplifiedRelayNodeView AddRelayNode(PortView inputPort, PortView outputPort, Vector2 position)
		{
			var relayNode = BaseNode.CreateFromType<SimplifiedRelayNode>(position);
			var view = (SimplifiedRelayNodeView)AddNode(relayNode);

			if (outputPort != null)
				Connect(outputPort, view.inputPortViews[0]);
			if (inputPort != null)
				Connect(view.outputPortViews[0], inputPort);

			return view;
		}

		/// <summary>
		/// Update all the serialized property bindings (in case a node was deleted / added, the property pathes needs to be updated)
		/// </summary>
		public void SyncSerializedPropertyPaths()
		{
			foreach (BaseNodeView nodeView in nodeViews)
				nodeView.SyncSerializedPropertyPaths();
			nodeInspector.RefreshNodes();
		}

		/// <summary>
		/// Call this function when you want to remove this view
		/// </summary>
		public void Dispose()
		{
			ClearGraphElements();
			RemoveFromHierarchy();
			Undo.undoRedoPerformed -= ReloadView;
			Object.DestroyImmediate(nodeInspector);

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
			HashSet<BaseNode> inSubgraph = viewsInSubgraph.Select(v => v.nodeTarget).ToHashSet();

			string assetPath = AssetDatabase.GetAssetPath(graph);
			string directory = System.IO.Path.GetDirectoryName(assetPath)!;
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
						SerializableEdge edge = edgeView.serializedEdge;
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
			HashSet<Type> nodeTypes = viewsInSubgraph.Select(v => v.nodeTarget.GetType()).ToHashSet();
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
			subgraphNodeView.UnserializeAndPasteOperation("create subgraph", copy);

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
				string parameterGuid = subgraph.AddSubgraphParameter(port.portData.displayName, port.portType, isInputParameter ? ParameterDirection.Input : ParameterDirection.Output);

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
					BaseNode copiedNode = subgraphNodeView._lastCopiedNodesMap[port.owner.nodeTarget.GUID];
					NodePort originPortInSubgraph = copiedNode.GetPort(port.fieldName, port.portData.identifier);

					// Connect the parameter nodes to their matching ports.
					if (isInputParameter)
					{
						NodePort from = parameterNode.outputPorts.First();
						subgraph.Connect(from, originPortInSubgraph);
					}
					else
					{
						NodePort to = parameterNode.inputPorts.First();
						subgraph.Connect(originPortInSubgraph, to);
					}
				}
			}

			AssetDatabase.CreateAsset(subgraph, subgraphPath);

			var subgraphNode = BaseNode.CreateFromType<SubgraphNode>(center);
			subgraphNode.Subgraph = subgraph;
			BaseNodeView view = AddNode(subgraphNode);

			// Hookup border edges to the subgraph node's ports.
			foreach (((PortView port, bool isInputParameter), var list) in borderEdges)
			{
				string parameter = parameterLookup[port];
				if (isInputParameter)
				{
					PortView to = view.GetPortViewFromFieldName(
						nameof(SubgraphNode.Inputs),
						parameter
					);
					foreach (EdgeView edge in list)
					{
						Connect((PortView)edge.output, to);
					}
				}
				else
				{
					PortView from = view.GetPortViewFromFieldName(
						nameof(SubgraphNode.Outputs),
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
		}

		private void UnpackSubgraph()
		{
			var subgraphNode = (SubgraphNode)selection.OfType<SubgraphNodeView>().First().nodeTarget;
			graph.InlineSubgraphNode(subgraphNode);
			graph.RemoveNode(subgraphNode);
			Initialize(graph); // Reload this completely.
		}
	}
}
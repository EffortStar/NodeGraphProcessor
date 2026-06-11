using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using UnityEditor;
using System.Reflection;
using System;
using System.Collections;
using System.Linq;
using UnityEditor.UIElements;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using UnityEngine.Pool;
using NodeView = UnityEditor.Experimental.GraphView.Node;

namespace GraphProcessor
{
	[NodeCustomEditor(typeof(BaseNode))]
	public class BaseNodeView : NodeView, IPositionableView
	{
		public const string UssClassName = "node";
		public const string IconUssClassName = UssClassName + "__icon";
		public const string ObsoleteUssClassName = UssClassName + "--obsolete";
		public const string HasErrorUssClassName = UssClassName + "--hasError";
		public const string PrototypeUssClassName = UssClassName + "--prototype";

		public const string TitleContainerName = "title";
		public BaseNode NodeTarget;
		private NodeProvider.NodeFlags _nodeFlags;

		public readonly List<PortView> InputPortViews = new();
		public readonly List<PortView> OutputPortViews = new();

		public IEnumerable<PortView> AllPortViews => InputPortViews.Concat(OutputPortViews);

		public BaseGraphView Owner { get; private set; }

		private readonly Dictionary<(string path, string identifier), PortView> _portViewLookup = new();

		public VisualElement ControlsContainer;
		protected VisualElement DebugContainer;
		protected VisualElement RightTitleContainer;
		protected VisualElement TopPortContainer;
		protected VisualElement BottomPortContainer;
		private VisualElement _inputContainerElement;

		private VisualElement _settings;
		private NodeSettingsView _settingsContainer;
		private Button _settingButton;
		private TextField _titleTextField;

		private VisualElement _titleIcon;
		private string _currentTitleIconClass;

		protected virtual bool HasSettings { get; set; }

		public bool Initializing; //Used for applying SetPosition on locked node at init.

		private const string BaseNodeStyle = "GraphProcessorStyles/BaseNodeView";

		private bool _settingsExpanded;

		private IconBadges _badges;

		private float _selectedNodesFarLeft;
		private float _selectedNodesNearLeft;
		private float _selectedNodesFarRight;
		private float _selectedNodesNearRight;
		private float _selectedNodesFarTop;
		private float _selectedNodesNearTop;
		private float _selectedNodesFarBottom;
		private float _selectedNodesNearBottom;
		private float _selectedNodesAvgHorizontal;
		private float _selectedNodesAvgVertical;

		/// <summary>
		/// Set a custom uss file for the node. We use a Resources.Load to get the stylesheet so be sure to put the correct resources path
		/// https://docs.unity3d.com/ScriptReference/Resources.Load.html
		/// </summary>
		public virtual string LayoutStyle => string.Empty;

		private bool _isUnmorphedGenericNode;

		public bool IsUnmorphedGenericNode(out Type baseTypeConstraint)
		{
			if (!_isUnmorphedGenericNode)
			{
				baseTypeConstraint = null;
				return false;
			}

			baseTypeConstraint =
				((GenericNodeAttribute)Attribute.GetCustomAttribute(
					NodeTarget.GetType(),
					typeof(GenericNodeAttribute)
				))
				.BaseConstraintType;
			return true;
		}

#region Initialization

		public BaseNodeView()
		{
			// Dragging support by clicking on IconBadges, to ease selection when errors are visible.
			RegisterCallback<MouseDownEvent, BaseNodeView>(static (e, args) =>
			{
				if (e.clickCount != 1 || e.target is not IconBadge badge)
				{
					return;
				}

				PortView view = args.Query<PortView>().Where(p => p.HasBadge(badge)).First();
				if (view == null)
				{
					return;
				}

				var connector = (BaseEdgeConnector)view.edgeConnector;
				connector.TryStartDragging(e);
			}, this);
		}

		public void Initialize(BaseGraphView owner, BaseNode node)
		{
			NodeTarget = node;
			Owner = owner;

			if (!node.deletable)
				capabilities &= ~Capabilities.Deletable;
			
			_isUnmorphedGenericNode = Attribute.IsDefined(node.GetType(), typeof(GenericNodeAttribute));

			node.OnMessageAdded += AddBadge;
			node.OnMessageRemoved += RemoveBadge;

			styleSheets.Add(Resources.Load<StyleSheet>(BaseNodeStyle));

			_nodeFlags = NodeProvider.GetNodeFlags(node.GetType());

			if (!string.IsNullOrEmpty(LayoutStyle))
				styleSheets.Add(Resources.Load<StyleSheet>(LayoutStyle));

			InitializeView();
			InitializePorts();
			InitializeDebug();

			try
			{
				// If the standard Enable method is still overwritten, we call it
				if (GetType().GetMethod(nameof(Enable), new Type[] { })?.DeclaringType != typeof(BaseNodeView))
					Enable();
				else
					Enable(false);
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}

			InitializeSettings();

			RefreshExpandedState();

			RefreshPorts();

			RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
			RegisterCallback<DetachFromPanelEvent>(_ =>
			{
				try
				{
					Disable();
				}
				catch (Exception e)
				{
					Debug.LogException(e);
				}
			});
			OnGeometryChanged(null);
		}

		private void InitializePorts()
		{
			BaseEdgeConnectorListener listener = Owner.ConnectorListener;

			foreach (NodePort inputPort in NodeTarget.InputPorts)
			{
				AddPort(inputPort, listener);
			}

			foreach (NodePort outputPort in NodeTarget.OutputPorts)
			{
				AddPort(outputPort, listener);
			}
		}

		protected virtual void InitializeView()
		{
			if ((_nodeFlags & NodeProvider.NodeFlags.Striped) != 0)
				this.Q(TitleContainerName).Insert(0, new StripedElement());

			ControlsContainer = new VisualElement { name = "controls" };
			ControlsContainer.AddToClassList("NodeControls");
			mainContainer.Add(ControlsContainer);

			RightTitleContainer = new VisualElement { name = "RightTitleContainer" };
			titleContainer.Add(RightTitleContainer);

			TopPortContainer = new VisualElement { name = "TopPortContainer" };
			Insert(0, TopPortContainer);

			BottomPortContainer = new VisualElement { name = "BottomPortContainer" };
			Add(BottomPortContainer);

			Undo.undoRedoPerformed += UpdateFieldValues;

			DebugContainer = new VisualElement { name = "debug" };
			if (NodeTarget.debug)
				mainContainer.Add(DebugContainer);

			Initializing = true;

			UpdateTitle();
			SetPosition(NodeTarget.position);

			AddInputContainer();
			
			if (NodeProvider.TryGetNodeColor(NodeTarget.GetType(), out Color color))
			{
				IStyle style = this.Q("node-border").style;
				style.borderTopColor = color;
				style.borderTopWidth = 4;
				_inputContainerElement.style.marginTop = 4;
			}

			_badges = new IconBadges(this, topContainer);

			if ((_nodeFlags & NodeProvider.NodeFlags.Obsolete) != 0)
			{
				AddToClassList(ObsoleteUssClassName);
				AddBadge($"Obsolete: {NodeTarget.GetType().GetCustomAttributes<ObsoleteAttribute>().First().Message}", BadgeMessageType.Error);
			}
			else if ((_nodeFlags & NodeProvider.NodeFlags.Prototype) != 0)
			{
				AddToClassList(PrototypeUssClassName);
				AddBadge("Prototype node may be changed or removed", BadgeMessageType.Warning);
			}
			
			if ((_nodeFlags & NodeProvider.NodeFlags.HasInfo) != 0)
			{
				AddBadge(NodeTarget.GetType().GetCustomAttributes<NodeInfoAttribute>().First().Message, BadgeMessageType.Info);
			}

			if ((_nodeFlags & NodeProvider.NodeFlags.SubgraphIncompatible) != 0 && Owner.graph.IsSubgraph)
			{
				AddToClassList(HasErrorUssClassName);
				AddBadge("This node is not supported in subgraphs.", BadgeMessageType.Error);
			}
		}
		
		protected virtual void RefreshAfterSetNodeTarget()
		{
			Owner.SerializedGraph.Update();
			_portViewLookup.Clear();
			// in
			PortView[] oldPortViewsIn = InputPortViews.ToArray();
			InputPortViews.Clear();
			inputContainer.Clear();
			_inputContainerElement.Clear();
			// out
			PortView[] oldPortViewsOut = OutputPortViews.ToArray();
			OutputPortViews.Clear();
			outputContainer.Clear();
			// other
			BottomPortContainer.Clear();
			ControlsContainer.Clear();
			_fieldControlsMap.Clear();
			UpdateTitle();
			InitializePorts();
			DrawDefaultInspector();
			ReassignPortViewEdges(oldPortViewsIn, InputPortViews);
			ReassignPortViewEdges(oldPortViewsOut, OutputPortViews);
			return;

			void ReassignPortViewEdges(PortView[] oldPortViews, List<PortView> newPortViews)
			{
				foreach (PortView oldPortView in oldPortViews)
				{
					foreach (PortView newPortView in newPortViews)
					{
						if (oldPortView.FieldPath != newPortView.FieldPath || oldPortView.Port.Identifier != newPortView.Port.Identifier) continue;
						foreach (EdgeView edgeView in oldPortView.GetEdges())
						{
							newPortView.Connect(edgeView);
						}
					}
				}
			}
		}

		protected void SetTitleIcon(string className)
		{
			if (_currentTitleIconClass != null)
				_titleIcon?.RemoveFromClassList(_currentTitleIconClass);

			if (className == null)
			{
				_titleIcon?.RemoveFromHierarchy();
				return;
			}

			_titleIcon ??= new VisualElement { name = "TitleIcon", pickingMode = PickingMode.Ignore };
			_titleIcon.AddToClassList(IconUssClassName);
			titleContainer.Insert(0, _titleIcon);
			_titleIcon.AddToClassList(_currentTitleIconClass = className);
		}

		protected void UpdateTitle()
		{
			string customName = NodeTarget.name;
			title = string.IsNullOrEmpty(customName) ? NodeTarget.GetType().Name : customName;
		}

		private void InitializeSettings()
		{
			if (!HasSettings)
				return;
			// Initialize settings button:
			CreateSettingButton();
			_settingsContainer = new NodeSettingsView { visible = false };
			_settings = new VisualElement();
			// Add Node type specific settings
			_settings.Add(CreateSettingsView());
			_settingsContainer.Add(_settings);
			Add(_settingsContainer);

			using var _ = ListPool<FieldInfo>.Get(out var fields);
			Type type = NodeTarget.GetType();
			do
			{
				fields.AddRange(
					type.GetFields(
						BindingFlags.Public 
						| BindingFlags.NonPublic 
						| BindingFlags.Instance
						| BindingFlags.DeclaredOnly
					)
				);
				type = type.BaseType;
			} while (type != null && type != typeof(BaseNode));

			foreach (FieldInfo field in fields)
			{
				if (Attribute.IsDefined(field, typeof(SettingAttribute)))
					AddSettingField(field);
			}

			_settingsContainer.Bind(Owner.SerializedGraph);
		}

		private void OnGeometryChanged(GeometryChangedEvent evt)
		{
			if (_settingButton != null)
			{
				Rect settingsButtonLayout = _settingButton.ChangeCoordinatesTo(_settingsContainer.parent, _settingButton.layout);
				_settingsContainer.style.top = settingsButtonLayout.yMax - 18f;
				_settingsContainer.style.left = settingsButtonLayout.xMin - layout.width + 20f;
			}
		}

		// Workaround for bug in GraphView that makes the node selection border way too big
		private VisualElement selectionBorder, nodeBorder;

		internal void EnableSyncSelectionBorderHeight()
		{
			if (selectionBorder == null || nodeBorder == null)
			{
				selectionBorder = this.Q("selection-border");
				nodeBorder = this.Q("node-border");

				schedule.Execute(() => { selectionBorder.style.height = nodeBorder.localBound.height; }).Every(17);
			}
		}

		private void CreateSettingButton()
		{
			_settingButton = new Button(ToggleSettings) { name = "settings-button" };
			_settingButton.Add(new Image { name = "icon", scaleMode = ScaleMode.ScaleToFit });

			titleContainer.Add(_settingButton);
		}

		private void ToggleSettings()
		{
			_settingsExpanded = !_settingsExpanded;
			if (_settingsExpanded)
				OpenSettings();
			else
				CloseSettings();
		}

		public void OpenSettings()
		{
			if (_settingsContainer != null)
			{
				Owner.ClearSelection();
				Owner.AddToSelection(this);

				_settingButton.AddToClassList("clicked");
				_settingsContainer.visible = true;
				_settingsExpanded = true;
			}
		}

		public void CloseSettings()
		{
			if (_settingsContainer != null)
			{
				_settingButton.RemoveFromClassList("clicked");
				_settingsContainer.visible = false;
				_settingsExpanded = false;
			}
		}

		private void InitializeDebug()
		{
		}

		#endregion

		#region API

		public PortView GetPortView(string fieldPath, string identifier)
		{
			identifier ??= "";
			_portViewLookup.TryGetValue((fieldPath, identifier), out PortView result);
			return result;
		}


		public PortView AddPort(NodePort port, BaseEdgeConnectorListener listener)
		{
			PortView p = CreatePortView(port, listener);

			if (p.direction == Direction.Input)
			{
				InputPortViews.Add(p);

				if (port.IsVertical)
					TopPortContainer.Add(p);
				else
					inputContainer.Add(p);
			}
			else
			{
				OutputPortViews.Add(p);

				if (port.IsVertical)
					BottomPortContainer.Add(p);
				else
					outputContainer.Add(p);
			}

			p.Initialize(this, port.EditorDisplayName);
			_portViewLookup[(p.FieldPath, p.Identifier)] = p;
			return p;
		}

		protected virtual PortView CreatePortView(NodePort port, BaseEdgeConnectorListener listener)
			=> PortView.CreatePortView(port, listener);

		private List<NodeView> GetValuesForSelectedNodes()
		{
			List<NodeView> selectedNodes = new();
			Owner.nodes.ForEach(node =>
			{
				if (node.selected) selectedNodes.Add(node);
			});

			if (selectedNodes.Count < 2) return selectedNodes; //	No need for any of the calculations below

			_selectedNodesFarLeft = int.MinValue;
			_selectedNodesFarRight = int.MinValue;
			_selectedNodesFarTop = int.MinValue;
			_selectedNodesFarBottom = int.MinValue;

			_selectedNodesNearLeft = int.MaxValue;
			_selectedNodesNearRight = int.MaxValue;
			_selectedNodesNearTop = int.MaxValue;
			_selectedNodesNearBottom = int.MaxValue;

			foreach (NodeView selectedNode in selectedNodes)
			{
				IStyle nodeStyle = selectedNode.style;
				float nodeWidth = selectedNode.localBound.size.x;
				float nodeHeight = selectedNode.localBound.size.y;

				if (nodeStyle.left.value.value > _selectedNodesFarLeft) _selectedNodesFarLeft = nodeStyle.left.value.value;
				if (nodeStyle.left.value.value + nodeWidth > _selectedNodesFarRight) _selectedNodesFarRight = nodeStyle.left.value.value + nodeWidth;
				if (nodeStyle.top.value.value > _selectedNodesFarTop) _selectedNodesFarTop = nodeStyle.top.value.value;
				if (nodeStyle.top.value.value + nodeHeight > _selectedNodesFarBottom) _selectedNodesFarBottom = nodeStyle.top.value.value + nodeHeight;

				if (nodeStyle.left.value.value < _selectedNodesNearLeft) _selectedNodesNearLeft = nodeStyle.left.value.value;
				if (nodeStyle.left.value.value + nodeWidth < _selectedNodesNearRight) _selectedNodesNearRight = nodeStyle.left.value.value + nodeWidth;
				if (nodeStyle.top.value.value < _selectedNodesNearTop) _selectedNodesNearTop = nodeStyle.top.value.value;
				if (nodeStyle.top.value.value + nodeHeight < _selectedNodesNearBottom) _selectedNodesNearBottom = nodeStyle.top.value.value + nodeHeight;
			}

			_selectedNodesAvgHorizontal = (_selectedNodesNearLeft + _selectedNodesFarRight) / 2f;
			_selectedNodesAvgVertical = (_selectedNodesNearTop + _selectedNodesFarBottom) / 2f;
			return selectedNodes;
		}

		public static Rect GetNodeRect(NodeView node, float left = int.MaxValue, float top = int.MaxValue)
		{
			return new Rect(
				new Vector2(left != int.MaxValue ? left : node.style.left.value.value, top != int.MaxValue ? top : node.style.top.value.value),
				new Vector2(node.style.width.value.value, node.style.height.value.value)
			);
		}

		public void AlignToLeft()
		{
			List<NodeView> selectedNodes = GetValuesForSelectedNodes();
			if (selectedNodes.Count < 2) return;

			foreach (NodeView selectedNode in selectedNodes)
			{
				selectedNode.SetPosition(GetNodeRect(selectedNode, _selectedNodesNearLeft));
			}
		}

		public void AlignToCenter()
		{
			List<NodeView> selectedNodes = GetValuesForSelectedNodes();
			if (selectedNodes.Count < 2) return;

			foreach (NodeView selectedNode in selectedNodes)
			{
				selectedNode.SetPosition(GetNodeRect(selectedNode, _selectedNodesAvgHorizontal - selectedNode.localBound.size.x / 2f));
			}
		}

		public void AlignToRight()
		{
			List<NodeView> selectedNodes = GetValuesForSelectedNodes();
			if (selectedNodes.Count < 2) return;

			foreach (NodeView selectedNode in selectedNodes)
			{
				selectedNode.SetPosition(GetNodeRect(selectedNode, _selectedNodesFarRight - selectedNode.localBound.size.x));
			}
		}

		public void AlignToTop()
		{
			List<NodeView> selectedNodes = GetValuesForSelectedNodes();
			if (selectedNodes.Count < 2) return;

			foreach (NodeView selectedNode in selectedNodes)
			{
				selectedNode.SetPosition(GetNodeRect(selectedNode, top: _selectedNodesNearTop));
			}
		}

		public void AlignToMiddle()
		{
			List<NodeView> selectedNodes = GetValuesForSelectedNodes();
			if (selectedNodes.Count < 2) return;

			foreach (NodeView selectedNode in selectedNodes)
			{
				selectedNode.SetPosition(GetNodeRect(selectedNode, top: _selectedNodesAvgVertical - selectedNode.localBound.size.y / 2f));
			}
		}

		public void AlignToBottom()
		{
			List<NodeView> selectedNodes = GetValuesForSelectedNodes();
			if (selectedNodes.Count < 2) return;

			foreach (NodeView selectedNode in selectedNodes)
			{
				selectedNode.SetPosition(GetNodeRect(selectedNode, top: _selectedNodesFarBottom - selectedNode.localBound.size.y));
			}
		}

		[PublicAPI]
		public void OpenNodeViewScript()
		{
			MonoScript script = NodeProvider.GetNodeViewScript(GetType());

			if (script != null)
			{
				AssetDatabase.OpenAsset(script.GetEntityId(), 0, 0);
				return;
			}

			foreach (MethodInfo method in GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
			{
				// There's no better fallback, believe me I've tried (to find the constructor, the type, etc).
				if (SourceUtility.OpenAtMethod(method))
					return;
			}
		}

		public void OpenNodeScript()
		{
			MonoScript script = NodeProvider.GetNodeScript(NodeTarget.GetType());

			if (script != null)
				AssetDatabase.OpenAsset(script.GetEntityId(), 0, 0);
			
			foreach (MethodInfo method in NodeTarget.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
			{
				if (SourceUtility.OpenAtMethod(method))
					return;
			}
		}

		public void ToggleDebug()
		{
			NodeTarget.debug = !NodeTarget.debug;
			UpdateDebugView();
		}

		public void UpdateDebugView()
		{
			if (NodeTarget.debug)
				mainContainer.Add(DebugContainer);
			else
				mainContainer.Remove(DebugContainer);
		}

		/// <summary>
		/// Adds a badge (an attached icon and message).
		/// </summary>
		public void AddBadge(string message, BadgeMessageType messageType) => _badges.AddBadge(message, messageType);
		
		/// <summary>
		/// Adds a badge (an attached icon and message).
		/// </summary>
		public void AddBadge(string message, string messageType) => _badges.AddBadge(message, messageType);

		/// <summary>
		/// Removes a badge matching the provided <paramref name="message" />.
		/// </summary>
		public void RemoveBadge(string message) => _badges.RemoveBadge(message);

		/// <summary>
		/// Removes all badges from this node and its ports.
		/// </summary>
		public void RemoveAllBadgesFromNodeAndPorts()
		{
			_badges.RemoveAllBadges();
			foreach (PortView port in AllPortViews)
			{
				port.RemoveAllBadges();
			}
		}

		public void Highlight()
		{
			AddToClassList("Highlight");
		}

		public void UnHighlight()
		{
			RemoveFromClassList("Highlight");
		}

		#endregion

		#region Callbacks & Overrides

		public virtual void Enable(bool fromInspector = false) => DrawDefaultInspector(fromInspector);
		public virtual void Enable() => DrawDefaultInspector(false);

		public virtual void Disable()
		{
		}

		private readonly Dictionary<string, List<(object value, VisualElement target)>> _visibleConditions = new();
		private readonly Dictionary<string, VisualElement> _hideElementIfConnected = new();
		private readonly Dictionary<FieldInfo, List<VisualElement>> _fieldControlsMap = new();

		public bool TryGetAssociatedControlField(PortView port, out PropertyField field)
		{
			if (!_hideElementIfConnected.TryGetValue(port.FieldPath, out VisualElement element) || element is not PropertyField result)
			{
				field = null;
				return false;
			}

			field = result;
			return true;
		}

		protected void AddInputContainer()
		{
			_inputContainerElement = new VisualElement { name = "input-container" };
			mainContainer.parent.Add(_inputContainerElement);
			_inputContainerElement.SendToBack();
			_inputContainerElement.pickingMode = PickingMode.Ignore;
		}

		protected virtual void DrawDefaultInspector(bool fromInspector = false)
		{
			using var _ = ListPool<FieldInfo>.Get(out var fields);
			Type type = NodeTarget.GetType();
			do
			{
				fields.AddRange(
					type.GetFields(
						BindingFlags.Public 
						| BindingFlags.NonPublic 
						| BindingFlags.Instance
						| BindingFlags.DeclaredOnly
					)
				);
				type = type.BaseType;
			} while (type != null && type != typeof(BaseNode));
			
			foreach (FieldInfo field in BaseNode.OverrideFieldOrder(fields).Reverse())
			{
				//skip if the field is a node setting
				if (Attribute.IsDefined(field, typeof(SettingAttribute)))
				{
					HasSettings = true;
					continue;
				}

				//skip if the field is not serializable
				bool serializeField = Attribute.IsDefined(field, typeof(SerializeField));
				if ((!field.IsPublic && !serializeField) || field.IsNotSerialized)
				{
					AddEmptyField(field, fromInspector);
					continue;
				}

				//skip if the field is an input/output and not marked as SerializedField
				var hasInputAttribute = Attribute.IsDefined(field, typeof(InputAttribute));
				var hasOutputAttribute = Attribute.IsDefined(field, typeof(OutputAttribute));
				bool hasInputOrOutputAttribute = hasInputAttribute || hasOutputAttribute;
				bool showAsDrawer = !fromInspector && Attribute.IsDefined(field, typeof(ShowAsDrawerAttribute));
				if (!serializeField && hasInputOrOutputAttribute && !showAsDrawer)
				{
					AddEmptyField(field, fromInspector);
					continue;
				}

				//skip if marked with NonSerialized or HideInInspector
				if (Attribute.IsDefined(field, typeof(NonSerializedAttribute)) || Attribute.IsDefined(field, typeof(HideInInspector)))
				{
					AddEmptyField(field, fromInspector);
					continue;
				}

				// Hide the field if we want to display in the inspector
				var showInInspector = field.GetCustomAttribute<ShowInInspectorAttribute>();
				if (!serializeField && showInInspector != null && !showInInspector.showInNode && !fromInspector)
				{
					AddEmptyField(field, false);
					continue;
				}

				bool showInputDrawer = hasInputAttribute && serializeField;
				showInputDrawer |= hasInputAttribute && Attribute.IsDefined(field, typeof(ShowAsDrawerAttribute));
				showInputDrawer &= !fromInspector; // We can't show a drawer in the inspector
				showInputDrawer &= !typeof(IList).IsAssignableFrom(field.FieldType);

				string displayName = ObjectNames.NicifyVariableName(field.Name);

				var inspectorNameAttribute = field.GetCustomAttribute<InspectorNameAttribute>();
				if (inspectorNameAttribute != null)
					displayName = inspectorNameAttribute.displayName;

				VisualElement elem = AddControlField(field, displayName, showInputDrawer);
				if (hasInputAttribute)
				{
					_hideElementIfConnected[field.Name] = elem;

					// Hide the field right away if there is already a connection:
					if (_portViewLookup.TryGetValue((field.Name, ""), out PortView pv))
					{
						if (pv.GetEdges().Count > 0)
							elem.style.display = DisplayStyle.None;
					}
				}
			}
		}

		private void AddEmptyField(FieldInfo field, bool fromInspector)
		{
			if (!Attribute.IsDefined(field, typeof(InputAttribute)) || fromInspector)
				return;

			if (Attribute.IsDefined(field, typeof(VerticalAttribute)))
				return;

			var box = new VisualElement { name = field.Name };
			box.AddToClassList("port-input-element");
			box.AddToClassList("empty");
			_inputContainerElement.Add(box);
		}

		private void UpdateFieldVisibility(string fieldName, object newValue)
		{
			if (newValue == null)
				return;
			if (_visibleConditions.TryGetValue(fieldName, out List<(object value, VisualElement target)> list))
			{
				foreach ((object value, VisualElement target) elem in list)
				{
					if (newValue.Equals(elem.value))
						elem.target.style.display = DisplayStyle.Flex;
					else
						elem.target.style.display = DisplayStyle.None;
				}
			}
		}

		private void UpdateOtherFieldValueSpecific<T>(FieldInfo field, object newValue)
		{
			foreach (VisualElement inputField in _fieldControlsMap[field])
			{
				if (inputField is INotifyValueChanged<T> notify)
					notify.SetValueWithoutNotify((T)newValue);
			}
		}

		private static readonly MethodInfo specificUpdateOtherFieldValue = typeof(BaseNodeView).GetMethod(nameof(UpdateOtherFieldValueSpecific), BindingFlags.NonPublic | BindingFlags.Instance);

		private void UpdateOtherFieldValue(FieldInfo info, object newValue)
		{
			// Warning: Keep in sync with FieldFactory CreateField
			Type fieldType = info.FieldType.IsSubclassOf(typeof(UnityEngine.Object)) ? typeof(UnityEngine.Object) : info.FieldType;
			MethodInfo genericUpdate = specificUpdateOtherFieldValue.MakeGenericMethod(fieldType);

			genericUpdate.Invoke(this, new[] { info, newValue });
		}

		private object GetInputFieldValueSpecific<T>(FieldInfo field)
		{
			if (_fieldControlsMap.TryGetValue(field, out List<VisualElement> list))
			{
				foreach (VisualElement inputField in list)
				{
					if (inputField is INotifyValueChanged<T> notify)
						return notify.value;
				}
			}

			return null;
		}

		private static readonly MethodInfo specificGetValue = typeof(BaseNodeView).GetMethod(nameof(GetInputFieldValueSpecific), BindingFlags.NonPublic | BindingFlags.Instance);

		private object GetInputFieldValue(FieldInfo info)
		{
			// Warning: Keep in sync with FieldFactory CreateField
			Type fieldType = info.FieldType.IsSubclassOf(typeof(UnityEngine.Object)) ? typeof(UnityEngine.Object) : info.FieldType;
			MethodInfo genericUpdate = specificGetValue.MakeGenericMethod(fieldType);

			return genericUpdate.Invoke(this, new object[] { info });
		}

		protected VisualElement AddControlField(string fieldName, string label = null, bool showInputDrawer = false, Action valueChangedCallback = null)
			=> AddControlField(NodeTarget.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance), label, showInputDrawer, valueChangedCallback);

		private readonly Regex s_ReplaceNodeIndexPropertyPath = new(@"(^nodes.Array.data\[)(\d+)(\])");

		internal void SyncSerializedPropertyPaths()
		{
			int nodeIndex = Owner.graph.nodes.FindIndex(n => n == NodeTarget);

			// If the node is not found, then it means that it has been deleted from serialized data.
			if (nodeIndex == -1)
				return;

			var nodeIndexString = nodeIndex.ToString();
			foreach (PropertyField propertyField in this.Query<PropertyField>().Build())
			{
				// Don't process nested property fields.
				if (propertyField.panel == null || propertyField.GetFirstAncestorOfType<PropertyField>() != null)
				{
					continue;
				}

				if (propertyField.bindingPath == null)
				{
					continue;
				}

				propertyField.Unbind();
				// The property path look like this: nodes.Array.data[x].fieldName
				// And we want to update the value of x with the new node index:
				propertyField.bindingPath = s_ReplaceNodeIndexPropertyPath.Replace(propertyField.bindingPath, m => m.Groups[1].Value + nodeIndexString + m.Groups[3].Value);
				propertyField.Bind(Owner.SerializedGraph);
			}
		}

		protected SerializedProperty FindSerializedProperty(string fieldName)
		{
			int i = Owner.graph.nodes.FindIndex(n => n == NodeTarget);
			return Owner.SerializedGraph.FindProperty("nodes").GetArrayElementAtIndex(i).FindPropertyRelative(fieldName);
		}

		protected VisualElement AddControlField(FieldInfo field, string label = null, bool showInputDrawer = false, Action valueChangedCallback = null)
		{
			if (field == null)
				return null;

			var element = new PropertyField(FindSerializedProperty(field.Name), showInputDrawer ? "" : label);
			element.Bind(Owner.SerializedGraph);

			if (typeof(IList).IsAssignableFrom(field.FieldType))
				EnableSyncSelectionBorderHeight();

			element.RegisterValueChangeCallback(e =>
			{
				UpdateFieldVisibility(field.Name, field.GetValue(NodeTarget));
				valueChangedCallback?.Invoke();
				NotifyNodeChanged();
				GetPortView(field.Name, "")?.PortViewValueChanged();
			});

			// Disallow picking scene objects when the graph is not linked to a scene
			if (!Owner.graph.IsLinkedToScene())
			{
				var objectField = element.Q<ObjectField>();
				if (objectField != null)
					objectField.allowSceneObjects = false;
			}

			if (!_fieldControlsMap.TryGetValue(field, out List<VisualElement> inputFieldList))
				inputFieldList = _fieldControlsMap[field] = new List<VisualElement>();
			inputFieldList.Add(element);

			if (showInputDrawer)
			{
				var box = new VisualElement { name = field.Name };
				box.AddToClassList("port-input-element");
				box.Add(element);
				_inputContainerElement.Add(box);
			}
			else
			{
				ControlsContainer.Add(element);
			}

			element.name = field.Name;

			if (field.GetCustomAttribute(typeof(VisibleIfAttribute)) is VisibleIfAttribute visibleCondition)
			{
				// Check if target field exists:
				FieldInfo conditionField = NodeTarget.GetType().GetField(visibleCondition.fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				if (conditionField == null)
					Debug.LogError($"[VisibleIf] Field {visibleCondition.fieldName} does not exists in node {NodeTarget.GetType()}");
				else
				{
					_visibleConditions.TryGetValue(visibleCondition.fieldName, out List<(object value, VisualElement target)> list);
					list ??= _visibleConditions[visibleCondition.fieldName] = new List<(object value, VisualElement target)>();
					list.Add((visibleCondition.value, element));
					UpdateFieldVisibility(visibleCondition.fieldName, conditionField.GetValue(NodeTarget));
				}
			}

			return element;
		}

		private void UpdateFieldValues()
		{
			foreach (KeyValuePair<FieldInfo, List<VisualElement>> kp in _fieldControlsMap)
				UpdateOtherFieldValue(kp.Key, kp.Key.GetValue(NodeTarget));
		}

		protected void AddSettingField(FieldInfo field)
		{
			if (field == null)
				return;

			string label = field.GetCustomAttribute<SettingAttribute>().name;

			var element = new PropertyField(FindSerializedProperty(field.Name), label);

			if (element != null)
			{
				_settingsContainer.Add(element);
				element.name = field.Name;
			}
		}

		internal void OnPortConnected(PortView port)
		{
			if (port.direction == Direction.Input && _inputContainerElement?.Q(port.FieldPath) != null)
				_inputContainerElement.Q(port.FieldPath).AddToClassList("empty");

			if (_hideElementIfConnected.TryGetValue(port.FieldPath, out VisualElement elem))
				elem.style.display = DisplayStyle.None;
		}

		internal void OnPortDisconnected(PortView port)
		{
			if (port.direction == Direction.Input && _inputContainerElement?.Q(port.FieldPath) != null)
			{
				_inputContainerElement.Q(port.FieldPath).RemoveFromClassList("empty");
				
				if (NodeInformation.TryGetInfo(NodeTarget.GetType(), port.FieldPath, out NodeFieldInformation fieldInfo))
				{
					object valueBeforeConnection = fieldInfo.GetValue(NodeTarget);

					if (valueBeforeConnection != null)
					{
						fieldInfo.SetValue(NodeTarget, valueBeforeConnection);
					}
				}
			}

			if (_hideElementIfConnected.TryGetValue(port.FieldPath, out VisualElement elem))
				elem.style.display = DisplayStyle.Flex;
		}

		// TODO: a function to force to reload the custom behavior ports (if we want to do a button to add ports for example)

		public virtual void OnRemoved()
		{
		}

		public virtual void OnCreated()
		{
		}

		public void SetPosition(Vector2 newPos)
		{
			SetPosition(new Rect(newPos, Vector2.zero)); // The rect size isn't actually used.
		}

		public override void SetPosition(Rect newPos)
		{
			base.SetPosition(newPos);

			if (!Initializing)
				Owner.RegisterCompleteObjectUndo("Moved graph node");

			NodeTarget.position = newPos.position;
			Initializing = false;
		}

		public Vector2 GetElementPosition() => NodeTarget.position;

		public override bool expanded
		{
			get => base.expanded;
			set
			{
				base.expanded = value;
				NodeTarget.expanded = value;
			}
		}

		public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
		{
			BuildAlignMenu(evt);
			evt.menu.AppendAction("Open Node Script", (e) => OpenNodeScript());
			// evt.menu.AppendAction("Open Node View Script", (e) => OpenNodeViewScript());
			// evt.menu.AppendAction("Debug", (e) => ToggleDebug(), DebugStatus); // TODO re-add if we ever use this.
		}

		protected void BuildAlignMenu(ContextualMenuPopulateEvent evt)
		{
			if (Owner.selection.OfType<BaseNodeView>().Count() < 2) return;
			evt.menu.AppendAction("Align/To Left", (e) => AlignToLeft());
			evt.menu.AppendAction("Align/To Center", (e) => AlignToCenter());
			evt.menu.AppendAction("Align/To Right", (e) => AlignToRight());
			evt.menu.AppendSeparator("Align/");
			evt.menu.AppendAction("Align/To Top", (e) => AlignToTop());
			evt.menu.AppendAction("Align/To Middle", (e) => AlignToMiddle());
			evt.menu.AppendAction("Align/To Bottom", (e) => AlignToBottom());
			evt.menu.AppendSeparator();
		}

		public bool MorphNodeToGenericNodeType(
			Type genericTypeArgument,
			bool solidifyType,
			bool refreshPorts = false
		)
		{
			BaseNode prevNode = NodeTarget;
			Type nodeType = prevNode.GetType();
			if (
				!Attribute.IsDefined(nodeType, typeof(GenericNodeAttribute))
				|| !nodeType.IsConstructedGenericType
				|| genericTypeArgument == typeof(object)
			)
			{
				return false;
			}

			var attribute = nodeType.GetCustomAttribute<GenericNodeAttribute>();
			Type genericNodeType = nodeType.GetGenericTypeDefinition();
			for (var i = 0; i < attribute.ExcludedTypes.Length; i++)
			{
				if (attribute.ExcludedTypes[i] != genericTypeArgument)
					continue;
					
				Debug.LogWarning($"{TypeUtility.FormatTypeName(genericNodeType)} does not support {TypeUtility.FormatTypeName(genericTypeArgument)} because {attribute.Reasons[i]}.");
				return false;
			}

			Type[] genericArgs = nodeType.GetGenericArguments();
			if (genericArgs.Length != 1)
			{
				Debug.LogWarning("Multiple generic args currently not supported for " + nameof(GenericNodeAttribute));
				return false;
			}

			if (genericArgs[0] != attribute.BaseConstraintType)
			{
				return false;
			}

			return MorphNodeToType(
				genericNodeType.MakeGenericType(genericTypeArgument),
				solidifyType: solidifyType,
				refreshPorts: refreshPorts
			);
		}

		/// <summary>
		/// Transform a node into a different type.
		/// </summary>
		/// <param name="toType">The type to transform the node into.</param>
		/// <param name="solidifyType">
		/// True if the node has been morphed into a specific type
		/// (it can no longer morph due to edge connections)
		/// </param>
		/// <param name="refreshPorts"><see cref="RefreshPorts"/></param>
		/// <returns>Whether the morph was successful.</returns>
		public bool MorphNodeToType(
			Type toType,
			bool solidifyType,
			bool refreshPorts = true
		)
		{
			if (!TryMakeSpecificGenericNode())
			{
				return false;
			}
			
			foreach (PortView portView in OutputPortViews)
			{
				foreach (EdgeView edgeView in portView.GetEdges())
				{
					edgeView.output = GetPortView(portView.FieldPath, portView.Port.Identifier);
					edgeView.OnPortChanged(false);
				}
			}
			
			foreach (PortView portView in InputPortViews)
			{
				foreach (EdgeView edgeView in portView.GetEdges())
				{
					edgeView.input = GetPortView(portView.FieldPath, portView.Port.Identifier);
					edgeView.OnPortChanged(true);
				}
			}

			if (refreshPorts)
			{
				RefreshPorts();
			}

			if (solidifyType)
			{
				_isUnmorphedGenericNode = false;
			}

			return true;

			bool TryMakeSpecificGenericNode()
			{
				try
				{
					BaseNode prevNode = NodeTarget;
					var instance = (BaseNode)Activator.CreateInstance(toType);
					FieldInfo[] fields = toType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
					foreach (FieldInfo field in fields)
					{
						if (field.IsInitOnly) continue;
						if (field.Name == "_customPortBehaviorMap") continue;
						try
						{
							field.SetValue(instance, field.GetValue(prevNode));
						}
						catch (Exception)
						{
							// Generic fields won't copy, but this is fine.
						}
					}

					NodeTarget = instance;
					Owner.graph.nodes.Remove(prevNode);
					Owner.graph.RemoveNodeFromCache(prevNode);
					Owner.graph.AddNode(instance);
					RefreshAfterSetNodeTarget();

					// Debug.Log($"Morph {genericArgs[0]} to {toType}");
					return true;
				}
				catch (Exception e)
				{
					Debug.LogException(e);
					return false;
				}
			}
		}

		public new virtual bool RefreshPorts()
		{
			// If a port behavior was attached to one port, then
			// the port count might have been updated by the node
			// so we have to refresh the list of port views.
			NodeTarget.RefreshCustomPorts();
			UpdatePortViewWithPorts(NodeTarget.InputPorts, InputPortViews);
			UpdatePortViewWithPorts(NodeTarget.OutputPorts, OutputPortViews);
			
			return base.RefreshPorts();

			void UpdatePortViewWithPorts(NodePortContainer ports, List<PortView> portViews)
			{
				for (var i = 0; i < portViews.Count; i++)
				{
					portViews[i].UpdatePortView(ports[i]);
				}
			}
		}

		protected virtual VisualElement CreateSettingsView() => new Label("Settings") { name = "header" };

		/// <summary>
		/// Send an event to the graph telling that the content of this node have changed
		/// </summary>
		public void NotifyNodeChanged() => Owner.graph.NotifyNodeChanged(NodeTarget);

		#endregion
	}
}
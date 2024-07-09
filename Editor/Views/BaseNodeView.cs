﻿using System.Collections.Generic;
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
using Status = UnityEngine.UIElements.DropdownMenuAction.Status;
using NodeView = UnityEditor.Experimental.GraphView.Node;

namespace GraphProcessor
{
	[NodeCustomEditor(typeof(BaseNode))]
	public class BaseNodeView : NodeView
	{
		public const string UssClassName = "node";
		public const string ObsoleteUssClassName = UssClassName + "--obsolete";
		public const string PrototypeUssClassName = UssClassName + "--prototype";
			
		public const string TitleContainerName = "title";
		
		public BaseNode nodeTarget;
		private NodeProvider.NodeFlags nodeFlags;

		public readonly List<PortView> inputPortViews = new();
		public readonly List<PortView> outputPortViews = new();

		public IEnumerable<PortView> AllPortViews => inputPortViews.Concat(outputPortViews);

		public BaseGraphView owner { private set; get; }

		protected readonly Dictionary<string, List<PortView>> portsPerFieldName = new();

		public VisualElement controlsContainer;
		protected VisualElement debugContainer;
		protected VisualElement rightTitleContainer;
		protected VisualElement topPortContainer;
		protected VisualElement bottomPortContainer;
		private VisualElement inputContainerElement;

		private VisualElement settings;
		private NodeSettingsView settingsContainer;
		private Button settingButton;
		private TextField titleTextField;
		
		public event Action<PortView> onPortConnected;
		public event Action<PortView> onPortDisconnected;

		protected virtual bool hasSettings { get; set; }

		public bool initializing = false; //Used for applying SetPosition on locked node at init.

		private readonly string baseNodeStyle = "GraphProcessorStyles/BaseNodeView";

		private bool settingsExpanded = false;

		private IconBadges badges;

		private List<Node> selectedNodes = new();
		private float selectedNodesFarLeft;
		private float selectedNodesNearLeft;
		private float selectedNodesFarRight;
		private float selectedNodesNearRight;
		private float selectedNodesFarTop;
		private float selectedNodesNearTop;
		private float selectedNodesFarBottom;
		private float selectedNodesNearBottom;
		private float selectedNodesAvgHorizontal;
		private float selectedNodesAvgVertical;
		
		/// <summary>
		/// Set a custom uss file for the node. We use a Resources.Load to get the stylesheet so be sure to put the correct resources path
		/// https://docs.unity3d.com/ScriptReference/Resources.Load.html
		/// </summary>
		public virtual string layoutStyle => string.Empty;

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
			nodeTarget = node;
			this.owner = owner;

			if (!node.deletable)
				capabilities &= ~Capabilities.Deletable;
			// Note that the Renamable capability is useless right now as it haven't been implemented in Graphview
			if (node.isRenamable)
				capabilities |= Capabilities.Renamable;

			node.onMessageAdded += AddBadge;
			node.onMessageRemoved += RemoveBadge;
			node.onPortsUpdated += a => schedule.Execute(_ => UpdatePortsForField(a)).ExecuteLater(0);

			styleSheets.Add(Resources.Load<StyleSheet>(baseNodeStyle));

			nodeFlags = NodeProvider.GetNodeFlags(node.GetType());

			if (!string.IsNullOrEmpty(layoutStyle))
				styleSheets.Add(Resources.Load<StyleSheet>(layoutStyle));

			InitializeView();
			InitializePorts();
			InitializeDebug();

			// If the standard Enable method is still overwritten, we call it
			if (GetType().GetMethod(nameof(Enable), new Type[] { })?.DeclaringType != typeof(BaseNodeView))
				ExceptionToLog.Call(() => Enable());
			else
				ExceptionToLog.Call(() => Enable(false));

			InitializeSettings();

			RefreshExpandedState();

			RefreshPorts();

			RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
			RegisterCallback<DetachFromPanelEvent>(e => ExceptionToLog.Call(Disable));
			OnGeometryChanged(null);
		}

		private void InitializePorts()
		{
			BaseEdgeConnectorListener listener = owner.connectorListener;

			foreach (NodePort inputPort in nodeTarget.inputPorts)
			{
				AddPort(inputPort.fieldInfo, Direction.Input, listener, inputPort.portData);
			}

			foreach (NodePort outputPort in nodeTarget.outputPorts)
			{
				AddPort(outputPort.fieldInfo, Direction.Output, listener, outputPort.portData);
			}
		}

		private void InitializeView()
		{
			if (nodeFlags != NodeProvider.NodeFlags.None)
				this.Q(TitleContainerName).Insert(0, new StripedElement());
			
			controlsContainer = new VisualElement { name = "controls" };
			controlsContainer.AddToClassList("NodeControls");
			mainContainer.Add(controlsContainer);

			rightTitleContainer = new VisualElement { name = "RightTitleContainer" };
			titleContainer.Add(rightTitleContainer);

			topPortContainer = new VisualElement { name = "TopPortContainer" };
			Insert(0, topPortContainer);

			bottomPortContainer = new VisualElement { name = "BottomPortContainer" };
			Add(bottomPortContainer);

			if (nodeTarget.showControlsOnHover)
			{
				bool mouseOverControls = false;
				controlsContainer.style.display = DisplayStyle.None;
				RegisterCallback<MouseOverEvent>(e =>
				{
					controlsContainer.style.display = DisplayStyle.Flex;
					mouseOverControls = true;
				});
				RegisterCallback<MouseOutEvent>(e =>
				{
					Rect rect = GetPosition();
					Vector2 graphMousePosition = owner.contentViewContainer.WorldToLocal(e.mousePosition);
					if (rect.Contains(graphMousePosition) || !nodeTarget.showControlsOnHover)
						return;
					mouseOverControls = false;
					schedule.Execute(_ =>
					{
						if (!mouseOverControls)
							controlsContainer.style.display = DisplayStyle.None;
					}).ExecuteLater(500);
				});
			}

			Undo.undoRedoPerformed += UpdateFieldValues;

			debugContainer = new VisualElement { name = "debug" };
			if (nodeTarget.debug)
				mainContainer.Add(debugContainer);

			initializing = true;

			UpdateTitle();
			SetPosition(nodeTarget.position);
			SetNodeColor(nodeTarget.color);

			AddInputContainer();

			badges = new IconBadges(this, topContainer);

			// Add renaming capability
			if ((capabilities & Capabilities.Renamable) != 0)
				SetupRenamableTitle();
			
			if ((nodeFlags & NodeProvider.NodeFlags.Obsolete) != 0)
			{
				AddToClassList(ObsoleteUssClassName);
				AddBadge($"Obsolete: {nodeTarget.GetType().GetCustomAttributes<ObsoleteAttribute>().First().Message}", BadgeMessageType.Error);
			}
			else if ((nodeFlags & NodeProvider.NodeFlags.Prototype) != 0)
			{
				AddToClassList(PrototypeUssClassName);
				AddBadge("Prototype node may be changed or removed", BadgeMessageType.Warning);
			}
		}

		private void SetupRenamableTitle()
		{
			var titleLabel = this.Q("title-label") as Label;

			titleTextField = new TextField { isDelayed = true };
			titleTextField.style.display = DisplayStyle.None;
			titleLabel.parent.Insert(0, titleTextField);

			titleLabel.RegisterCallback<MouseDownEvent>(e =>
			{
				if (e.clickCount == 2 && e.button == (int)MouseButton.LeftMouse)
					OpenTitleEditor();
			});

			titleTextField.RegisterValueChangedCallback(e => CloseAndSaveTitleEditor(e.newValue));

			titleTextField.RegisterCallback<MouseDownEvent>(e =>
			{
				if (e.clickCount == 2 && e.button == (int)MouseButton.LeftMouse)
					CloseAndSaveTitleEditor(titleTextField.value);
			});

			titleTextField.RegisterCallback<FocusOutEvent>(e => CloseAndSaveTitleEditor(titleTextField.value));

			void OpenTitleEditor()
			{
				// show title textbox
				titleTextField.style.display = DisplayStyle.Flex;
				titleLabel.style.display = DisplayStyle.None;
				titleTextField.focusable = true;

				titleTextField.SetValueWithoutNotify(title);
				titleTextField.Focus();
				titleTextField.SelectAll();
			}

			void CloseAndSaveTitleEditor(string newTitle)
			{
				owner.RegisterCompleteObjectUndo("Renamed node " + newTitle);
				nodeTarget.SetCustomName(newTitle);

				// hide title TextBox
				titleTextField.style.display = DisplayStyle.None;
				titleLabel.style.display = DisplayStyle.Flex;
				titleTextField.focusable = false;

				UpdateTitle();
			}
		}

		private void UpdateTitle()
		{
			title = nodeTarget.GetCustomName() == null ? nodeTarget.GetType().Name : nodeTarget.GetCustomName();
		}

		private void InitializeSettings()
		{
			if (!hasSettings)
				return;
			// Initialize settings button:
			CreateSettingButton();
			settingsContainer = new NodeSettingsView { visible = false };
			settings = new VisualElement();
			// Add Node type specific settings
			settings.Add(CreateSettingsView());
			settingsContainer.Add(settings);
			Add(settingsContainer);

			FieldInfo[] fields = nodeTarget.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

			foreach (FieldInfo field in fields)
			{
				if (Attribute.IsDefined(field, typeof(SettingAttribute)))
					AddSettingField(field);
			}
		}

		private void OnGeometryChanged(GeometryChangedEvent evt)
		{
			if (settingButton != null)
			{
				Rect settingsButtonLayout = settingButton.ChangeCoordinatesTo(settingsContainer.parent, settingButton.layout);
				settingsContainer.style.top = settingsButtonLayout.yMax - 18f;
				settingsContainer.style.left = settingsButtonLayout.xMin - layout.width + 20f;
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
			settingButton = new Button(ToggleSettings) { name = "settings-button" };
			settingButton.Add(new Image { name = "icon", scaleMode = ScaleMode.ScaleToFit });

			titleContainer.Add(settingButton);
		}

		private void ToggleSettings()
		{
			settingsExpanded = !settingsExpanded;
			if (settingsExpanded)
				OpenSettings();
			else
				CloseSettings();
		}

		public void OpenSettings()
		{
			if (settingsContainer != null)
			{
				owner.ClearSelection();
				owner.AddToSelection(this);

				settingButton.AddToClassList("clicked");
				settingsContainer.visible = true;
				settingsExpanded = true;
			}
		}

		public void CloseSettings()
		{
			if (settingsContainer != null)
			{
				settingButton.RemoveFromClassList("clicked");
				settingsContainer.visible = false;
				settingsExpanded = false;
			}
		}

		private void InitializeDebug()
		{
		}

		#endregion

		#region API

		public List<PortView> GetPortViewsFromFieldName(string fieldName)
		{
			List<PortView> ret;

			portsPerFieldName.TryGetValue(fieldName, out ret);

			return ret;
		}

		public PortView GetFirstPortViewFromFieldName(string fieldName)
		{
			return GetPortViewsFromFieldName(fieldName)?.First();
		}

		public PortView GetPortViewFromFieldName(string fieldName, string identifier)
		{
			return GetPortViewsFromFieldName(fieldName)?.FirstOrDefault(pv => { return (pv.portData.identifier == identifier) || (String.IsNullOrEmpty(pv.portData.identifier) && String.IsNullOrEmpty(identifier)); });
		}


		public PortView AddPort(FieldInfo fieldInfo, Direction direction, BaseEdgeConnectorListener listener, PortData portData)
		{
			PortView p = CreatePortView(direction, fieldInfo, portData, listener);

			if (p.direction == Direction.Input)
			{
				inputPortViews.Add(p);

				if (portData.vertical)
					topPortContainer.Add(p);
				else
					inputContainer.Add(p);
			}
			else
			{
				outputPortViews.Add(p);

				if (portData.vertical)
					bottomPortContainer.Add(p);
				else
					outputContainer.Add(p);
			}

			p.Initialize(this, portData?.displayName);

			List<PortView> ports;
			portsPerFieldName.TryGetValue(p.fieldName, out ports);
			if (ports == null)
			{
				ports = new List<PortView>();
				portsPerFieldName[p.fieldName] = ports;
			}

			ports.Add(p);

			return p;
		}

		protected virtual PortView CreatePortView(Direction direction, FieldInfo fieldInfo, PortData portData, BaseEdgeConnectorListener listener)
			=> PortView.CreatePortView(direction, fieldInfo, portData, listener);

		public void InsertPort(PortView portView, int index)
		{
			if (portView.direction == Direction.Input)
			{
				if (portView.portData.vertical)
					topPortContainer.Insert(index, portView);
				else
					inputContainer.Insert(index, portView);
			}
			else
			{
				if (portView.portData.vertical)
					bottomPortContainer.Insert(index, portView);
				else
					outputContainer.Insert(index, portView);
			}
		}

		public void RemovePort(PortView p)
		{
			// Remove all connected edges:
			List<EdgeView> edgesCopy = p.GetEdges().ToList();
			foreach (EdgeView e in edgesCopy)
				owner.Disconnect(e, refreshPorts: false);

			if (p.direction == Direction.Input)
			{
				if (inputPortViews.Remove(p))
					p.RemoveFromHierarchy();
			}
			else
			{
				if (outputPortViews.Remove(p))
					p.RemoveFromHierarchy();
			}

			List<PortView> ports;
			portsPerFieldName.TryGetValue(p.fieldName, out ports);
			ports.Remove(p);
		}

		private void SetValuesForSelectedNodes()
		{
			selectedNodes = new List<Node>();
			owner.nodes.ForEach(node =>
			{
				if (node.selected) selectedNodes.Add(node);
			});

			if (selectedNodes.Count < 2) return; //	No need for any of the calculations below

			selectedNodesFarLeft = int.MinValue;
			selectedNodesFarRight = int.MinValue;
			selectedNodesFarTop = int.MinValue;
			selectedNodesFarBottom = int.MinValue;

			selectedNodesNearLeft = int.MaxValue;
			selectedNodesNearRight = int.MaxValue;
			selectedNodesNearTop = int.MaxValue;
			selectedNodesNearBottom = int.MaxValue;

			foreach (Node selectedNode in selectedNodes)
			{
				IStyle nodeStyle = selectedNode.style;
				float nodeWidth = selectedNode.localBound.size.x;
				float nodeHeight = selectedNode.localBound.size.y;

				if (nodeStyle.left.value.value > selectedNodesFarLeft) selectedNodesFarLeft = nodeStyle.left.value.value;
				if (nodeStyle.left.value.value + nodeWidth > selectedNodesFarRight) selectedNodesFarRight = nodeStyle.left.value.value + nodeWidth;
				if (nodeStyle.top.value.value > selectedNodesFarTop) selectedNodesFarTop = nodeStyle.top.value.value;
				if (nodeStyle.top.value.value + nodeHeight > selectedNodesFarBottom) selectedNodesFarBottom = nodeStyle.top.value.value + nodeHeight;

				if (nodeStyle.left.value.value < selectedNodesNearLeft) selectedNodesNearLeft = nodeStyle.left.value.value;
				if (nodeStyle.left.value.value + nodeWidth < selectedNodesNearRight) selectedNodesNearRight = nodeStyle.left.value.value + nodeWidth;
				if (nodeStyle.top.value.value < selectedNodesNearTop) selectedNodesNearTop = nodeStyle.top.value.value;
				if (nodeStyle.top.value.value + nodeHeight < selectedNodesNearBottom) selectedNodesNearBottom = nodeStyle.top.value.value + nodeHeight;
			}

			selectedNodesAvgHorizontal = (selectedNodesNearLeft + selectedNodesFarRight) / 2f;
			selectedNodesAvgVertical = (selectedNodesNearTop + selectedNodesFarBottom) / 2f;
		}

		public static Rect GetNodeRect(Node node, float left = int.MaxValue, float top = int.MaxValue)
		{
			return new Rect(
				new Vector2(left != int.MaxValue ? left : node.style.left.value.value, top != int.MaxValue ? top : node.style.top.value.value),
				new Vector2(node.style.width.value.value, node.style.height.value.value)
			);
		}

		public void AlignToLeft()
		{
			SetValuesForSelectedNodes();
			if (selectedNodes.Count < 2) return;

			foreach (Node selectedNode in selectedNodes)
			{
				selectedNode.SetPosition(GetNodeRect(selectedNode, selectedNodesNearLeft));
			}
		}

		public void AlignToCenter()
		{
			SetValuesForSelectedNodes();
			if (selectedNodes.Count < 2) return;

			foreach (Node selectedNode in selectedNodes)
			{
				selectedNode.SetPosition(GetNodeRect(selectedNode, selectedNodesAvgHorizontal - selectedNode.localBound.size.x / 2f));
			}
		}

		public void AlignToRight()
		{
			SetValuesForSelectedNodes();
			if (selectedNodes.Count < 2) return;

			foreach (Node selectedNode in selectedNodes)
			{
				selectedNode.SetPosition(GetNodeRect(selectedNode, selectedNodesFarRight - selectedNode.localBound.size.x));
			}
		}

		public void AlignToTop()
		{
			SetValuesForSelectedNodes();
			if (selectedNodes.Count < 2) return;

			foreach (Node selectedNode in selectedNodes)
			{
				selectedNode.SetPosition(GetNodeRect(selectedNode, top: selectedNodesNearTop));
			}
		}

		public void AlignToMiddle()
		{
			SetValuesForSelectedNodes();
			if (selectedNodes.Count < 2) return;

			foreach (Node selectedNode in selectedNodes)
			{
				selectedNode.SetPosition(GetNodeRect(selectedNode, top: selectedNodesAvgVertical - selectedNode.localBound.size.y / 2f));
			}
		}

		public void AlignToBottom()
		{
			SetValuesForSelectedNodes();
			if (selectedNodes.Count < 2) return;

			foreach (Node selectedNode in selectedNodes)
			{
				selectedNode.SetPosition(GetNodeRect(selectedNode, top: selectedNodesFarBottom - selectedNode.localBound.size.y));
			}
		}

		public void OpenNodeViewScript()
		{
			MonoScript script = NodeProvider.GetNodeViewScript(GetType());

			if (script != null)
				AssetDatabase.OpenAsset(script.GetInstanceID(), 0, 0);
		}

		public void OpenNodeScript()
		{
			MonoScript script = NodeProvider.GetNodeScript(nodeTarget.GetType());

			if (script != null)
				AssetDatabase.OpenAsset(script.GetInstanceID(), 0, 0);
		}

		public void ToggleDebug()
		{
			nodeTarget.debug = !nodeTarget.debug;
			UpdateDebugView();
		}

		public void UpdateDebugView()
		{
			if (nodeTarget.debug)
				mainContainer.Add(debugContainer);
			else
				mainContainer.Remove(debugContainer);
		}

		/// <summary>
		/// Adds a badge (an attached icon and message).
		/// </summary>
		public void AddBadge(string message, BadgeMessageType messageType) => badges.AddBadge(message, messageType);

		/// <summary>
		/// Removes a badge matching the provided <paramref name="message" />.
		/// </summary>
		public void RemoveBadge(string message) => badges.RemoveBadge(message);

		/// <summary>
		/// Removes all badges from this node and its ports.
		/// </summary>
		public void RemoveAllBadgesFromNodeAndPorts()
		{
			badges.RemoveAllBadges();
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

		private readonly Dictionary<string, List<(object value, VisualElement target)>> visibleConditions = new();
		private readonly Dictionary<string, VisualElement> hideElementIfConnected = new();
		private readonly Dictionary<FieldInfo, List<VisualElement>> fieldControlsMap = new();

		public bool TryGetAssociatedControlField(PortView port, out PropertyField field)
		{
			if (!hideElementIfConnected.TryGetValue(port.fieldName, out VisualElement element) || element is not PropertyField result)
			{
				field = null;
				return false;
			}

			field = result;
			return true;
		}

		protected void AddInputContainer()
		{
			inputContainerElement = new VisualElement { name = "input-container" };
			mainContainer.parent.Add(inputContainerElement);
			inputContainerElement.SendToBack();
			inputContainerElement.pickingMode = PickingMode.Ignore;
		}

		protected virtual void DrawDefaultInspector(bool fromInspector = false)
		{
			IEnumerable<FieldInfo> fields = nodeTarget.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
				// Filter fields from the BaseNode type since we are only interested in user-defined fields
				// (better than BindingFlags.DeclaredOnly because we keep any inherited user-defined fields) 
				.Where(f => f.DeclaringType != typeof(BaseNode));

			fields = BaseNode.OverrideFieldOrder(fields).Reverse();

			foreach (FieldInfo field in fields)
			{
				//skip if the field is a node setting
				if (Attribute.IsDefined(field, typeof(SettingAttribute)))
				{
					hasSettings = true;
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
					AddEmptyField(field, fromInspector);
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
					hideElementIfConnected[field.Name] = elem;

					// Hide the field right away if there is already a connection:
					if (portsPerFieldName.TryGetValue(field.Name, out List<PortView> pvs))
						if (pvs.Any(pv => pv.GetEdges().Count > 0))
							elem.style.display = DisplayStyle.None;
				}
			}
		}

		protected virtual void SetNodeColor(Color color)
		{
			titleContainer.style.borderBottomColor = new StyleColor(color);
			titleContainer.style.borderBottomWidth = new StyleFloat(color.a > 0 ? 5f : 0f);
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
			inputContainerElement.Add(box);
		}

		private void UpdateFieldVisibility(string fieldName, object newValue)
		{
			if (newValue == null)
				return;
			if (visibleConditions.TryGetValue(fieldName, out List<(object value, VisualElement target)> list))
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
			foreach (VisualElement inputField in fieldControlsMap[field])
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

			genericUpdate.Invoke(this, new object[] { info, newValue });
		}

		private object GetInputFieldValueSpecific<T>(FieldInfo field)
		{
			if (fieldControlsMap.TryGetValue(field, out List<VisualElement> list))
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
			=> AddControlField(nodeTarget.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance), label, showInputDrawer, valueChangedCallback);

		private readonly Regex s_ReplaceNodeIndexPropertyPath = new(@"(^nodes.Array.data\[)(\d+)(\])");

		internal void SyncSerializedPropertyPaths()
		{
			int nodeIndex = owner.graph.nodes.FindIndex(n => n == nodeTarget);

			// If the node is not found, then it means that it has been deleted from serialized data.
			if (nodeIndex == -1)
				return;

			var nodeIndexString = nodeIndex.ToString();
			foreach (PropertyField propertyField in this.Query<PropertyField>().ToList())
			{
				propertyField.Unbind();
				// The property path look like this: nodes.Array.data[x].fieldName
				// And we want to update the value of x with the new node index:
				propertyField.bindingPath = s_ReplaceNodeIndexPropertyPath.Replace(propertyField.bindingPath, m => m.Groups[1].Value + nodeIndexString + m.Groups[3].Value);
				propertyField.Bind(owner.serializedGraph);
			}
		}

		protected SerializedProperty FindSerializedProperty(string fieldName)
		{
			int i = owner.graph.nodes.FindIndex(n => n == nodeTarget);
			return owner.serializedGraph.FindProperty("nodes").GetArrayElementAtIndex(i).FindPropertyRelative(fieldName);
		}

		protected VisualElement AddControlField(FieldInfo field, string label = null, bool showInputDrawer = false, Action valueChangedCallback = null)
		{
			if (field == null)
				return null;

			var element = new PropertyField(FindSerializedProperty(field.Name), showInputDrawer ? "" : label);
			element.Bind(owner.serializedGraph);

			if (typeof(IList).IsAssignableFrom(field.FieldType))
				EnableSyncSelectionBorderHeight();

			element.RegisterValueChangeCallback(e =>
			{
				UpdateFieldVisibility(field.Name, field.GetValue(nodeTarget));
				valueChangedCallback?.Invoke();
				NotifyNodeChanged();
				GetPortViewFromFieldName(field.Name, "")?.PortViewValueChanged();
			});

			// Disallow picking scene objects when the graph is not linked to a scene
			if (!owner.graph.IsLinkedToScene())
			{
				var objectField = element.Q<ObjectField>();
				if (objectField != null)
					objectField.allowSceneObjects = false;
			}

			if (!fieldControlsMap.TryGetValue(field, out List<VisualElement> inputFieldList))
				inputFieldList = fieldControlsMap[field] = new List<VisualElement>();
			inputFieldList.Add(element);

			if (showInputDrawer)
			{
				var box = new VisualElement { name = field.Name };
				box.AddToClassList("port-input-element");
				box.Add(element);
				inputContainerElement.Add(box);
			}
			else
			{
				controlsContainer.Add(element);
			}

			element.name = field.Name;

			if (field.GetCustomAttribute(typeof(VisibleIfAttribute)) is VisibleIfAttribute visibleCondition)
			{
				// Check if target field exists:
				FieldInfo conditionField = nodeTarget.GetType().GetField(visibleCondition.fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				if (conditionField == null)
					Debug.LogError($"[VisibleIf] Field {visibleCondition.fieldName} does not exists in node {nodeTarget.GetType()}");
				else
				{
					visibleConditions.TryGetValue(visibleCondition.fieldName, out List<(object value, VisualElement target)> list);
					list ??= visibleConditions[visibleCondition.fieldName] = new List<(object value, VisualElement target)>();
					list.Add((visibleCondition.value, element));
					UpdateFieldVisibility(visibleCondition.fieldName, conditionField.GetValue(nodeTarget));
				}
			}

			return element;
		}

		private void UpdateFieldValues()
		{
			foreach (KeyValuePair<FieldInfo, List<VisualElement>> kp in fieldControlsMap)
				UpdateOtherFieldValue(kp.Key, kp.Key.GetValue(nodeTarget));
		}

		protected void AddSettingField(FieldInfo field)
		{
			if (field == null)
				return;

			string label = field.GetCustomAttribute<SettingAttribute>().name;

			var element = new PropertyField(FindSerializedProperty(field.Name));
			element.Bind(owner.serializedGraph);

			if (element != null)
			{
				settingsContainer.Add(element);
				element.name = field.Name;
			}
		}

		internal void OnPortConnected(PortView port)
		{
			if (port.direction == Direction.Input && inputContainerElement?.Q(port.fieldName) != null)
				inputContainerElement.Q(port.fieldName).AddToClassList("empty");

			if (hideElementIfConnected.TryGetValue(port.fieldName, out VisualElement elem))
				elem.style.display = DisplayStyle.None;

			onPortConnected?.Invoke(port);
		}

		internal void OnPortDisconnected(PortView port)
		{
			if (port.direction == Direction.Input && inputContainerElement?.Q(port.fieldName) != null)
			{
				inputContainerElement.Q(port.fieldName).RemoveFromClassList("empty");

				if (nodeTarget.nodeFields.TryGetValue(port.fieldName, out BaseNode.NodeFieldInformation fieldInfo))
				{
					object valueBeforeConnection = GetInputFieldValue(fieldInfo.info);

					if (valueBeforeConnection != null)
					{
						fieldInfo.info.SetValue(nodeTarget, valueBeforeConnection);
					}
				}
			}

			if (hideElementIfConnected.TryGetValue(port.fieldName, out VisualElement elem))
				elem.style.display = DisplayStyle.Flex;

			onPortDisconnected?.Invoke(port);
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
			if (initializing)
			{
				base.SetPosition(newPos);

				if (!initializing)
					owner.RegisterCompleteObjectUndo("Moved graph node");

				nodeTarget.position = newPos.position;
				initializing = false;
			}
		}

		public override bool expanded
		{
			get => base.expanded;
			set
			{
				base.expanded = value;
				nodeTarget.expanded = value;
			}
		}

		public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
		{
			BuildAlignMenu(evt);
			evt.menu.AppendAction("Open Node Script", (e) => OpenNodeScript(), OpenNodeScriptStatus);
			evt.menu.AppendAction("Open Node View Script", (e) => OpenNodeViewScript(), OpenNodeViewScriptStatus);
			evt.menu.AppendAction("Debug", (e) => ToggleDebug(), DebugStatus);
		}

		protected void BuildAlignMenu(ContextualMenuPopulateEvent evt)
		{
			evt.menu.AppendAction("Align/To Left", (e) => AlignToLeft());
			evt.menu.AppendAction("Align/To Center", (e) => AlignToCenter());
			evt.menu.AppendAction("Align/To Right", (e) => AlignToRight());
			evt.menu.AppendSeparator("Align/");
			evt.menu.AppendAction("Align/To Top", (e) => AlignToTop());
			evt.menu.AppendAction("Align/To Middle", (e) => AlignToMiddle());
			evt.menu.AppendAction("Align/To Bottom", (e) => AlignToBottom());
			evt.menu.AppendSeparator();
		}

		private Status LockStatus(DropdownMenuAction action)
		{
			return Status.Normal;
		}

		private Status DebugStatus(DropdownMenuAction action)
		{
			if (nodeTarget.debug)
				return Status.Checked;
			return Status.Normal;
		}

		private Status OpenNodeScriptStatus(DropdownMenuAction action)
		{
			if (NodeProvider.GetNodeScript(nodeTarget.GetType()) != null)
				return Status.Normal;
			return Status.Disabled;
		}

		private Status OpenNodeViewScriptStatus(DropdownMenuAction action)
		{
			if (NodeProvider.GetNodeViewScript(GetType()) != null)
				return Status.Normal;
			return Status.Disabled;
		}

		private IEnumerable<PortView> SyncPortCounts(IEnumerable<NodePort> ports, IEnumerable<PortView> portViews)
		{
			BaseEdgeConnectorListener listener = owner.connectorListener;
			List<PortView> portViewList = portViews.ToList();

			// Maybe not good to remove ports as edges are still connected :/
			foreach (PortView pv in portViews.ToList())
			{
				// If the port have disappeared from the node data, we remove the view:
				// We can use the identifier here because this function will only be called when there is a custom port behavior
				if (!ports.Any(p => p.portData.identifier == pv.portData.identifier))
				{
					RemovePort(pv);
					portViewList.Remove(pv);
				}
			}

			foreach (NodePort p in ports)
			{
				// Add missing port views
				if (!portViews.Any(pv => p.portData.identifier == pv.portData.identifier))
				{
					Direction portDirection = nodeTarget.IsFieldInput(p.fieldName) ? Direction.Input : Direction.Output;
					PortView pv = AddPort(p.fieldInfo, portDirection, listener, p.portData);
					portViewList.Add(pv);
				}
			}

			return portViewList;
		}

		private void SyncPortOrder(IEnumerable<NodePort> ports, IEnumerable<PortView> portViews)
		{
			List<PortView> portViewList = portViews.ToList();
			List<NodePort> portsList = ports.ToList();

			// Re-order the port views to match the ports order in case a custom behavior re-ordered the ports
			for (int i = 0; i < portsList.Count; i++)
			{
				string id = portsList[i].portData.identifier;

				PortView pv = portViewList.FirstOrDefault(p => p.portData.identifier == id);
				if (pv != null)
					InsertPort(pv, i);
			}
		}

		public virtual new bool RefreshPorts()
		{
			// If a port behavior was attached to one port, then
			// the port count might have been updated by the node
			// so we have to refresh the list of port views.
			UpdatePortViewWithPorts(nodeTarget.inputPorts, inputPortViews);
			UpdatePortViewWithPorts(nodeTarget.outputPorts, outputPortViews);

			void UpdatePortViewWithPorts(NodePortContainer ports, List<PortView> portViews)
			{
				if (ports.Count == 0 && portViews.Count == 0) // Nothing to update
					return;

				// When there is no current portviews, we can't zip the list so we just add all
				if (portViews.Count == 0)
					SyncPortCounts(ports, new PortView[] { });
				else if (ports.Count == 0) // Same when there is no ports
					SyncPortCounts(new NodePort[] { }, portViews);
				else if (portViews.Count != ports.Count)
					SyncPortCounts(ports, portViews);
				else
				{
					IEnumerable<IGrouping<string, NodePort>> p = ports.GroupBy(n => n.fieldName);
					IEnumerable<IGrouping<string, PortView>> pv = portViews.GroupBy(v => v.fieldName);
					p.Zip(pv, (portPerFieldName, portViewPerFieldName) =>
					{
						IEnumerable<PortView> portViewsList = portViewPerFieldName;
						if (portPerFieldName.Count() != portViewPerFieldName.Count())
							portViewsList = SyncPortCounts(portPerFieldName, portViewPerFieldName);
						SyncPortOrder(portPerFieldName, portViewsList);
						// We don't care about the result, we just iterate over port and portView
						return "";
					}).ToList();
				}

				// Here we're sure that we have the same amount of port and portView
				// so we can update the view with the new port data (if the name of a port have been changed for example)

				for (int i = 0; i < portViews.Count; i++)
					portViews[i].UpdatePortView(ports[i].portData);
			}

			return base.RefreshPorts();
		}

		public void ForceUpdatePorts()
		{
			nodeTarget.UpdateAllPorts();

			RefreshPorts();
		}

		private void UpdatePortsForField(string fieldName)
		{
			// TODO: actual code
			RefreshPorts();
		}

		protected virtual VisualElement CreateSettingsView() => new Label("Settings") { name = "header" };

		/// <summary>
		/// Send an event to the graph telling that the content of this node have changed
		/// </summary>
		public void NotifyNodeChanged() => owner.graph.NotifyNodeChanged(nodeTarget);

		#endregion
	}
}
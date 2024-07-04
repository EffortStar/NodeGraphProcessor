using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using System;
using System.Reflection;
using UnityEditor.UIElements;

namespace GraphProcessor
{
	public class PortView : Port
	{
		public string fieldName => fieldInfo.Name;
		public Type fieldType => fieldInfo.FieldType;
		public new Type portType;
		public BaseNodeView owner { get; private set; }
		public PortData portData;

		public event Action<PortView, Edge> OnConnected;
		public event Action<PortView, Edge> OnDisconnected;

		protected FieldInfo fieldInfo;
		protected BaseEdgeConnectorListener listener;

		private const string UserPortStyleFile = "PortViewTypes";

		private readonly List<EdgeView> edges = new List<EdgeView>();

		private const string PortStyle = "GraphProcessorStyles/PortView";
		private const string PortRequirementMessage = "Port is required";

		private IconBadges badges;
		private IVisualElementScheduledItem _scheduledBadgeEvent;

		protected PortView(Direction direction, FieldInfo fieldInfo, PortData portData, BaseEdgeConnectorListener edgeConnectorListener)
			: base(portData.vertical ? Orientation.Vertical : Orientation.Horizontal, direction, Capacity.Multi, portData.displayType ?? fieldInfo.FieldType)
		{
			this.fieldInfo = fieldInfo;
			listener = edgeConnectorListener;
			portType = portData.displayType ?? fieldInfo.FieldType;
			this.portData = portData;
			portName = fieldName;

			styleSheets.Add(Resources.Load<StyleSheet>(PortStyle));

			UpdatePortSize();

			var userPortStyle = Resources.Load<StyleSheet>(UserPortStyleFile);
			if (userPortStyle != null)
				styleSheets.Add(userPortStyle);

			if (portData.vertical)
				AddToClassList("Vertical");

			tooltip = portData.tooltip;
		}

		public static PortView CreatePortView(Direction direction, FieldInfo fieldInfo, PortData portData, BaseEdgeConnectorListener edgeConnectorListener)
		{
			var pv = new PortView(direction, fieldInfo, portData, edgeConnectorListener);
			pv.m_EdgeConnector = new BaseEdgeConnector(edgeConnectorListener);
			pv.AddManipulator(pv.m_EdgeConnector);

			// Force picking in the port label to enlarge the edge creation zone
			VisualElement portLabel = pv.Q("type");
			if (portLabel != null)
			{
				portLabel.pickingMode = PickingMode.Position;
				portLabel.style.flexGrow = 1;
			}

			// hide label when the port is vertical
			if (portData.vertical && portLabel != null)
				portLabel.style.display = DisplayStyle.None;

			// Fixup picking mode for vertical top ports
			if (portData.vertical)
				pv.Q("connector").pickingMode = PickingMode.Position;

			return pv;
		}

		/// <summary>
		/// Update the size of the port view (using the portData.sizeInPixel property)
		/// </summary>
		public void UpdatePortSize()
		{
			int size = portData.sizeInPixel == 0 ? 8 : portData.sizeInPixel;
			VisualElement connector = this.Q("connector");
			VisualElement cap = connector.Q("cap");
			connector.style.width = size;
			connector.style.height = size;
			cap.style.width = size - 4;
			cap.style.height = size - 4;

			// Update connected edge sizes:
			edges.ForEach(e => e.UpdateEdgeSize());
		}

		public virtual void Initialize(BaseNodeView nodeView, string name)
		{
			owner = nodeView;
			AddToClassList(fieldName);

			// Correct port type if port accept multiple values (and so is a container)
			if (direction == Direction.Input && portData.acceptMultipleEdges && portType == fieldType) // If the user haven't set a custom field type
			{
				if (fieldType.GetGenericArguments().Length > 0)
					portType = fieldType.GetGenericArguments()[0];
			}

			if (name != null)
				portName = name;
			visualClass = UssUtility.PortVisualClass(portType);
			tooltip = portData.tooltip;

			badges = new IconBadges(nodeView, m_ConnectorBoxCap);
		}

		public override void Connect(Edge edge)
		{
			bool wasPreviouslyConnected = edges.Count != 0;

			OnConnected?.Invoke(this, edge);

			base.Connect(edge);

			BaseNodeView inputNode = ((PortView)edge.input).owner;
			BaseNodeView outputNode = ((PortView)edge.output).owner;

			edges.Add(edge as EdgeView);

			inputNode.OnPortConnected((PortView)edge.input);
			outputNode.OnPortConnected((PortView)edge.output);

			if (!wasPreviouslyConnected && portData.required)
			{
				_scheduledBadgeEvent?.Pause();
				_scheduledBadgeEvent = schedule.Execute(() => RemoveBadge(PortRequirementMessage));
			}
		}

		public override void Disconnect(Edge edge)
		{
			OnDisconnected?.Invoke(this, edge);

			base.Disconnect(edge);

			if (!((EdgeView)edge).isConnected)
				return;

			BaseNodeView inputNode = (edge.input as PortView)?.owner;
			BaseNodeView outputNode = (edge.output as PortView)?.owner;

			inputNode?.OnPortDisconnected(edge.input as PortView);
			outputNode?.OnPortDisconnected(edge.output as PortView);

			edges.Remove(edge as EdgeView);

			if (FailedPortRequirement(out _) && portData.required)
			{
				_scheduledBadgeEvent?.Pause();
				_scheduledBadgeEvent = schedule.Execute(() => AddBadge(PortRequirementMessage, BadgeMessageType.Error));
			}
		}

		private enum FailureReason
		{
			NoEdges,
			PropertyFieldNotInitialized,
			PropertyIsDefault
		}

		private bool FailedPortRequirement(out FailureReason reason)
		{
			reason = FailureReason.NoEdges;
			if (edges.Count != 0)
				return false;
			
			if (!owner.TryGetAssociatedControlField(this, out PropertyField field))
				return true;

			if (field.childCount == 0)
			{
				reason = FailureReason.PropertyFieldNotInitialized;
				return true;
			}
			
			reason = FailureReason.PropertyIsDefault;
			switch (field[0])
			{
				case ObjectField objectField:
					return objectField.value == null;
				case TextField textField:
					return string.IsNullOrEmpty(textField.value);
				case PopupField<string> popup:
					return popup.index == 0;
				case EnumFlagsField flagsField:
					return flagsField.value.GetHashCode() == 0; // Thanks for this C#.
				case IntegerField intField:
					return intField.value == 0;
				case FloatField floatField:
					return floatField.value == 0;
			}
			return false; // TODO not yet supported on this serialized field type.
		}

		public void PortViewValueChanged()
		{
			if (portData.required)
			{
				_scheduledBadgeEvent = schedule.Execute(() =>
				{
					if (FailedPortRequirement(out _))
					{
						AddBadge(PortRequirementMessage, BadgeMessageType.Error);
					}
					else
					{
						RemoveBadge(PortRequirementMessage);
					}
				});
			}
		}

		public void UpdatePortView(PortData data)
		{
			if (data.displayType != null)
			{
				base.portType = data.displayType;
				portType = data.displayType;
				visualClass = UssUtility.PortVisualClass(portType);
			}

			if (!string.IsNullOrEmpty(data.displayName))
				portName = data.displayName;

			portData = data;

			// Update the edge in case the port color have changed
			schedule.Execute(() =>
			{
				foreach (EdgeView edge in edges)
				{
					edge.UpdateEdgeControl();
					edge.MarkDirtyRepaint();
				}
			}).ExecuteLater(50); // Hummm

			UpdatePortSize();
			
			if (portData.required)
			{
				RetryPortReqsUntilExists();
			}
			
			return;
			void RetryPortReqsUntilExists()
			{
				if (!FailedPortRequirement(out FailureReason reason))
				{
					RemoveBadge(PortRequirementMessage);
					return;
				}

				if (reason == FailureReason.PropertyFieldNotInitialized)
				{
					_scheduledBadgeEvent = schedule.Execute(RetryPortReqsUntilExists);
					return;
				}
				AddBadge(PortRequirementMessage, BadgeMessageType.Error);
			}
		}

		public List<EdgeView> GetEdges() => edges;

		/// <summary>
		/// Adds a badge (an attached icon and message) to this port.
		/// </summary>
		public void AddBadge(string message, BadgeMessageType messageType)
		{
			SpriteAlignment alignment = (direction, portData.vertical) switch
			{
				(Direction.Input, true) => SpriteAlignment.TopCenter,
				(Direction.Input, false) => SpriteAlignment.LeftCenter,
				(Direction.Output, true) => SpriteAlignment.BottomCenter,
				(Direction.Output, false) => SpriteAlignment.RightCenter,
				_ => throw new ArgumentOutOfRangeException()
			};

			badges.AddBadge(message, messageType, alignment);
		}

		/// <summary>
		/// Removes a badge matching the provided <paramref name="message" /> from the port.
		/// </summary>
		public void RemoveBadge(string message) => badges.RemoveBadge(message);

		/// <summary>
		/// Removes all badges from the port.
		/// </summary>
		public void RemoveAllBadges() => badges.RemoveAllBadges();
	}
}
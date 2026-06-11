using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using System;
using UnityEditor.UIElements;

namespace GraphProcessor
{
	public sealed class PortView : Port
	{
		public string FieldPath => Port.FieldPath;
		public Type FieldType => Port.DisplayType;
		public Type PortType { get; private set; }
		public BaseNodeView Owner { get; private set; }
		public NodePort Port;
		
		public const string UserPortStyleFile = "PortViewTypes";

		private readonly List<EdgeView> _edges = new();

		private const string PortStyle = "GraphProcessorStyles/PortView";
		private const string PortRequirementMessage = "Port is required";

		private IconBadges _badges;
		private IVisualElementScheduledItem _scheduledBadgeEvent;

		private PortView(NodePort port)
			: base(port.IsVertical ? Orientation.Vertical : Orientation.Horizontal, port.IsInput ? Direction.Input : Direction.Output, Capacity.Multi, port.DisplayType)
		{
			Port = port;
			PortType = Port.DisplayType;
			portName = FieldPath;

			styleSheets.Add(Resources.Load<StyleSheet>(PortStyle));

			UpdatePortSize();

			var userPortStyle = Resources.Load<StyleSheet>(UserPortStyleFile);
			if (userPortStyle != null)
				styleSheets.Add(userPortStyle);

			if (port.IsVertical)
				AddToClassList("Vertical");
			
			tooltip = port.EditorOnly.Tooltip;
		}

		public static PortView CreatePortView(NodePort port, BaseEdgeConnectorListener edgeConnectorListener)
		{
			var pv = new PortView(port)
			{
				m_EdgeConnector = new BaseEdgeConnector(edgeConnectorListener)
			};
			pv.AddManipulator(pv.m_EdgeConnector);

			// Force picking in the port label to enlarge the edge creation zone
			VisualElement portLabel = pv.Q("type");
			if (portLabel != null)
			{
				portLabel.pickingMode = PickingMode.Position;
				portLabel.style.flexGrow = 1;
			}

			// hide label when the port is vertical
			if (port.IsVertical && portLabel != null)
				portLabel.style.display = DisplayStyle.None;

			// Fixup picking mode for vertical top ports
			if (port.IsVertical)
				pv.Q("connector").pickingMode = PickingMode.Position;

			return pv;
		}

		/// <summary>
		/// Update the size of the port view (using the portData.sizeInPixel property)
		/// </summary>
		public void UpdatePortSize()
		{
			const int size = 8;
			VisualElement connector = this.Q("connector");
			VisualElement cap = connector.Q("cap");
			connector.style.width = size;
			connector.style.height = size;
			cap.style.width = size - 4;
			cap.style.height = size - 4;
		}

		public void Initialize(BaseNodeView nodeView, string name)
		{
			Owner = nodeView;
			AddToClassList(FieldPath);

			// Correct port type if port accept multiple values (and so is a container)
			if (direction == Direction.Input && Port.AllowMultipleEdges && PortType == FieldType) // If the user haven't set a custom field type
			{
				if (FieldType.GetGenericArguments().Length > 0)
					PortType = FieldType.GetGenericArguments()[0];
			}

			if (name != null)
				portName = name;
			visualClass = UssUtility.PortVisualClass(PortType);
			_badges = new IconBadges(nodeView, m_ConnectorBoxCap);
			
#if UNITY_EDITOR
			tooltip = Port.EditorOnly.Tooltip;
			if ((Port.EditorOnly.Flags & EditorOnlyPortInfo.FieldFlags.Obsolete) != 0)
			{
				this.Q<Label>().style.color = Color.indianRed;
				_badges.AddBadge("Obsolete", BadgeMessageType.Warning, SpriteAlignment.RightCenter);
			}
#endif
		}

		public override void Connect(Edge edge)
		{
			bool wasPreviouslyConnected = _edges.Count != 0;
			
			base.Connect(edge);

			BaseNodeView inputNode = ((PortView)edge.input).Owner;
			BaseNodeView outputNode = ((PortView)edge.output).Owner;

			_edges.Add(edge as EdgeView);

			inputNode.OnPortConnected((PortView)edge.input);
			outputNode.OnPortConnected((PortView)edge.output);

			if (!wasPreviouslyConnected && Port.IsRequired)
			{
				_scheduledBadgeEvent?.Pause();
				_scheduledBadgeEvent = schedule.Execute(() => RemoveBadge(PortRequirementMessage));
			}
		}

		public override void Disconnect(Edge edge)
		{
			base.Disconnect(edge);

			if (!((EdgeView)edge).isConnected)
				return;

			BaseNodeView inputNode = (edge.input as PortView)?.Owner;
			BaseNodeView outputNode = (edge.output as PortView)?.Owner;

			inputNode?.OnPortDisconnected(edge.input as PortView);
			outputNode?.OnPortDisconnected(edge.output as PortView);

			_edges.Remove((EdgeView)edge);

			if (FailedPortRequirement(out _) && Port.IsRequired)
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
			if (_edges.Count != 0)
				return false;
			
			if (!Owner.TryGetAssociatedControlField(this, out PropertyField field))
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
			if (Port.IsRequired)
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

		public void UpdatePortView(NodePort port)
		{
			if (port.DisplayType != null)
			{
				portType = port.DisplayType;
				PortType = port.DisplayType;
				visualClass = UssUtility.PortVisualClass(PortType);
			}

			if (!string.IsNullOrEmpty(port.EditorDisplayName))
				portName = port.EditorDisplayName;

			Port = port;

			// Update the edge in case the port color have changed
			schedule.Execute(() =>
			{
				foreach (EdgeView edge in _edges)
				{
					edge.UpdateEdgeControl();
					edge.MarkDirtyRepaint();
				}
			}).ExecuteLater(50); // Hummm

			UpdatePortSize();
			
			if (Port.IsRequired)
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
					_scheduledBadgeEvent?.Pause();
					_scheduledBadgeEvent = schedule.Execute(RetryPortReqsUntilExists);
					return;
				}
				AddBadge(PortRequirementMessage, BadgeMessageType.Error);
			}
		}

		public List<EdgeView> GetEdges() => _edges;

		/// <summary>
		/// Adds a badge (an attached icon and message) to this port.
		/// </summary>
		public void AddBadge(string message, BadgeMessageType messageType)
		{
			RemoveBadge(message);
			
			SpriteAlignment alignment = (direction, vertical: Port.IsVertical) switch
			{
				(Direction.Input, true) => SpriteAlignment.TopCenter,
				(Direction.Input, false) => SpriteAlignment.LeftCenter,
				(Direction.Output, true) => SpriteAlignment.BottomCenter,
				(Direction.Output, false) => SpriteAlignment.RightCenter,
				_ => throw new ArgumentOutOfRangeException()
			};

			_badges.AddBadge(message, messageType, alignment);
		}

		/// <summary>
		/// Removes a badge matching the provided <paramref name="message" /> from the port.
		/// </summary>
		public void RemoveBadge(string message) => _badges.RemoveBadge(message);

		/// <summary>
		/// Removes all badges from the port.
		/// </summary>
		public void RemoveAllBadges() => _badges.RemoveAllBadges();

		/// <summary>
		/// Tests whether a badge has been added to this element.
		/// </summary>
		public bool HasBadge(IconBadge badge) => _badges.Contains(badge);
	}
}
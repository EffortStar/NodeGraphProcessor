using System;
using System.Collections.Generic;
using System.Linq;

namespace GraphProcessor
{
	public sealed class SimplifiedRelayPortData : PortData
	{
		private readonly SimplifiedRelayNode _node;

		public override Type DisplayType
		{
			get => GetRelayType();
			set => throw new NotImplementedException();
		}

		public override bool AllowMultipleEdges
		{
			get
			{
				
#if UNITY_EDITOR
				// ReSharper disable once InvertIf
				if (IsInput)
				{
					Type displayType = DisplayType;
					return displayType == typeof(object) || Attribute.IsDefined(displayType, typeof(MultipleInputsRelayTypeAttribute));
				}
#endif
				// Never clean up SimplifiedRelayNode in builds.
				return true;
			}
			set => throw new NotImplementedException();
		}


		private Type GetRelayType()
		{
			if (IsInput)
			{
				SimplifiedRelayNode node = _node;
				do
				{
					if (node.InputPorts.FirstOrDefault() is not { } port)
						break;

					if (port.Edges.FirstOrDefault() is not { } edge)
						break;

					if (edge.FromNode is not SimplifiedRelayNode next)
					{
						return edge.FromPort.DisplayType;
					}

					node = next;
				} while (node != _node);
			}
			else
			{
				SimplifiedRelayNode node = _node;
				do
				{
					if (node.OutputPorts.FirstOrDefault() is not { } port)
						break;

					if (port.Edges.FirstOrDefault() is not { } edge)
						break;

					if (edge.ToNode is not SimplifiedRelayNode next)
					{
						return edge.ToPort.DisplayType;
					}

					node = next;
				} while (node != _node);
			}
			
			return typeof(object);
		}

		public SimplifiedRelayPortData(SimplifiedRelayNode node) => _node = node;
	}

	/// <summary>
	/// A relay node that's simple and just handled manually in the processor.
	/// </summary>
	[Serializable]
	public sealed class SimplifiedRelayNode : BaseNode
	{
		public const string InputPortKey = "In";
		public const string OutputPortKey = "Out";

		[CustomPortBehavior]
		private IEnumerable<PortData> InputPortBehavior()
		{
			yield return new SimplifiedRelayPortData(this)
			{
				Path = InputPortKey,
				IsRequired = true,
				IsInput = true
			};
		}

		[CustomPortBehavior]
		private IEnumerable<PortData> OutputPortBehavior()
		{
			// Default dummy port to avoid having a relay without any output:
			yield return new SimplifiedRelayPortData(this)
			{
				Path = OutputPortKey,
				IsRequired = true,
				IsInput = false
			};
		}
	}
}
using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

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
			return IsInput
				? GetInputType() ?? GetOutputType() ?? typeof(object)
				: GetOutputType() ?? GetInputType() ?? typeof(object);

			[CanBeNull]
			Type GetInputType()
			{
				SimplifiedRelayNode node = _node;
				do
				{
					if (node.InputPorts.FirstOrDefault() is not { } port)
						return null;

					if (port.Edges.FirstOrDefault() is not { } edge)
						return null;

					if (edge.FromNode is not SimplifiedRelayNode next)
						return edge.FromPort.DisplayType;

					node = next;
				} while (node != _node);

				return null;
			}

			[CanBeNull]
			Type GetOutputType()
			{
				SimplifiedRelayNode node = _node;
				do
				{
					if (node.OutputPorts.FirstOrDefault() is not { } port)
						return null;

					if (port.Edges.FirstOrDefault() is not { } edge)
						return null;

					if (edge.ToNode is not SimplifiedRelayNode next)
						return edge.ToPort.DisplayType;

					node = next;
				} while (node != _node);

				return null;
			}
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
			
			// Default dummy port to avoid having a relay without any output:
			yield return new SimplifiedRelayPortData(this)
			{
				Path = InputPortKey,
				IsRequired = true,
				IsInput = true
			};
			
			yield return new SimplifiedRelayPortData(this)
			{
				Path = OutputPortKey,
				IsRequired = true,
				IsInput = false
			};
		}
	}
}
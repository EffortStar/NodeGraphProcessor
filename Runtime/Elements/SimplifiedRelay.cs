using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace GraphProcessor
{
	/// <summary>
	/// A relay node that's simple and just handled manually in the processor.
	/// </summary>
	[Serializable]
	public sealed class SimplifiedRelayNode : BaseNode
	{
		public const string InputPortKey = "In";
		public const string OutputPortKey = "Out";
		
		private Type GetRelayType() =>
			inputPorts.FirstOrDefault()?.Edges.FirstOrDefault()?.FromPort.PortData.DisplayType
			?? outputPorts.FirstOrDefault()?.Edges.FirstOrDefault()?.ToPort.PortData.DisplayType
			?? typeof(object);

		[CustomPortBehavior(InputPortKey)]
		private IEnumerable<PortData> InputPortBehavior()
		{
			Type type = GetRelayType();
#if UNITY_EDITOR
			var acceptMultipleEdges = false;
			if (type != typeof(object) && Attribute.IsDefined(type, typeof(MultipleInputsRelayTypeAttribute)))
				acceptMultipleEdges = true;
#endif
			
			yield return new PortData
			{
				DisplayType = type,
#if UNITY_EDITOR
				AcceptMultipleEdges = acceptMultipleEdges,
#else
				// Never clean up SimplifiedRelayNode in builds.
				acceptMultipleEdges = true,
#endif
				Required = true
			};
		}

		[CustomPortBehavior(OutputPortKey)]
		private IEnumerable<PortData> OutputPortBehavior()
		{
			// Default dummy port to avoid having a relay without any output:
			yield return new PortData
			{
				DisplayType = GetRelayType(),
				AcceptMultipleEdges = true,
				Required = true
			};
		}
	}
}
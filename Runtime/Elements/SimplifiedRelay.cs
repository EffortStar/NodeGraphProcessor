using System;
using System.Collections.Generic;
using System.Linq;

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
			InputPorts.FirstOrDefault()?.Edges.FirstOrDefault()?.FromPort.DisplayType
			?? OutputPorts.FirstOrDefault()?.Edges.FirstOrDefault()?.ToPort.DisplayType
			?? typeof(object);

		[CustomPortBehavior]
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
				Path = InputPortKey,
				DisplayType = type,
#if UNITY_EDITOR
				AllowMultipleEdges = acceptMultipleEdges,
#else
				// Never clean up SimplifiedRelayNode in builds.
				acceptMultipleEdges = true,
#endif
				IsRequired = true,
				IsInput = true
			};
		}

		[CustomPortBehavior]
		private IEnumerable<PortData> OutputPortBehavior()
		{
			// Default dummy port to avoid having a relay without any output:
			yield return new PortData
			{
				Path = OutputPortKey,
				DisplayType = GetRelayType(),
				AllowMultipleEdges = true,
				IsRequired = true,
				IsInput = false
			};
		}
	}
}
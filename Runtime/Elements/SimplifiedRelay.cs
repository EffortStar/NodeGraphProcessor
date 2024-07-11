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
		[Input, RequiredPort]
		public object In;

		[Output, RequiredPort]
		public object Out;

		protected override void Process() => Out = In;

		private Type GetRelayType() =>
			inputPorts.FirstOrDefault()?.GetEdges().FirstOrDefault()?.FromPort.portData.displayType
			?? outputPorts.FirstOrDefault()?.GetEdges().FirstOrDefault()?.ToPort.portData.displayType
			?? typeof(object);

		[CustomPortBehavior(nameof(In)), UsedImplicitly]
		private IEnumerable<PortData> InputPortBehavior(List<SerializableEdge> edges)
		{
			var acceptMultipleEdges = false;
			Type type = GetRelayType();
#if UNITY_EDITOR
			if (type != typeof(object) && Attribute.IsDefined(type, typeof(MultipleInputsRelayTypeAttribute)))
				acceptMultipleEdges = true;
#endif
			
			yield return new PortData
			{
				displayType = type,
				acceptMultipleEdges = acceptMultipleEdges,
				required = true
			};
		}

		[CustomPortBehavior(nameof(Out)), UsedImplicitly]
		private IEnumerable<PortData> OutputPortBehavior(List<SerializableEdge> edges)
		{
			// Default dummy port to avoid having a relay without any output:
			yield return new PortData
			{
				displayType = GetRelayType(),
				acceptMultipleEdges = true,
				required = true
			};
		}
	}
}
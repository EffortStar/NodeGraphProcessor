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
			inputPorts.FirstOrDefault()?.Edges.FirstOrDefault()?.FromPort.PortData.displayType
			?? outputPorts.FirstOrDefault()?.Edges.FirstOrDefault()?.ToPort.PortData.displayType
			?? typeof(object);

		[CustomPortBehavior(nameof(In)), UsedImplicitly]
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
				displayType = type,
#if UNITY_EDITOR
				acceptMultipleEdges = acceptMultipleEdges,
#else
				// Never clean up SimplifiedRelayNode in builds.
				acceptMultipleEdges = true,
#endif
				required = true
			};
		}

		[CustomPortBehavior(nameof(Out)), UsedImplicitly]
		private IEnumerable<PortData> OutputPortBehavior()
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
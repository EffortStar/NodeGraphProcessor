using System;
using System.Collections.Generic;
using UnityEngine;

namespace GraphProcessor
{
	[Serializable]
	public class ParameterNode : BaseNode
	{
		[Input]
		public object input;

		[Output]
		public object output;

		public override string name => "Parameter";

		// We serialize the GUID of the exposed parameter in the graph so we can retrieve the true ExposedParameter from the graph
		[SerializeField, HideInInspector]
		public string parameterGUID;

		public SubgraphParameter parameter { get; private set; }

		public event Action onParameterChanged;

		public ParameterAccessor accessor;

		protected override void Enable()
		{
			// load the parameter
			LoadExposedParameter();

			graph.onSubgraphParameterModified += OnParamChanged;
			if (onParameterChanged != null)
				onParameterChanged?.Invoke();
		}

		void LoadExposedParameter()
		{
			parameter = graph.GetSubgraphParameterFromGUID(parameterGUID);

			if (parameter == null)
			{
				Debug.Log("Property \"" + parameterGUID + "\" Can't be found !");

				// Delete this node as the property can't be found
				graph.RemoveNode(this);
			}
		}

		void OnParamChanged(SubgraphParameter modifiedParam)
		{
			if (parameter == modifiedParam)
			{
				onParameterChanged?.Invoke();
			}
		}

		[CustomPortBehavior(nameof(output))]
		IEnumerable<PortData> GetOutputPort(List<SerializableEdge> edges)
		{
			if (accessor == ParameterAccessor.Get)
			{
				yield return new PortData
				{
					identifier = "output",
					displayName = "Value",
					displayType = (parameter == null) ? typeof(object) : parameter.GetValueType(),
					acceptMultipleEdges = true
				};
			}
		}

		[CustomPortBehavior(nameof(input))]
		IEnumerable<PortData> GetInputPort(List<SerializableEdge> edges)
		{
			if (accessor == ParameterAccessor.Set)
			{
				yield return new PortData
				{
					identifier = "input",
					displayName = "Value",
					displayType = (parameter == null) ? typeof(object) : parameter.GetValueType(),
				};
			}
		}

		protected override void Process() => throw new NotImplementedException("Parameters should be expanded when a SubGraph is instanced.");
	}

	public enum ParameterAccessor
	{
		Get,
		Set
	}
}

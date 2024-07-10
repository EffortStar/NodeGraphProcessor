using System;
using System.Collections.Generic;
using UnityEngine;

namespace GraphProcessor
{
	[Serializable]
	public sealed class ParameterNode : BaseNode
	{
		[Input]
		public object input;

		[Output]
		public object output;

		public override string name => "Parameter";

		// We serialize the GUID of the exposed parameter in the graph so we can retrieve the true ExposedParameter from the graph
		[SerializeField, HideInInspector]
		public string parameterGUID;

		public SubgraphParameter Parameter { get; private set; }

		public event Action onParameterChanged;
		
		protected override void Enable()
		{
			// load the parameter
			LoadExposedParameter();

			graph.onSubgraphParameterModified += OnParamChanged;
			onParameterChanged?.Invoke();
		}

		private void LoadExposedParameter()
		{
			Parameter = graph.GetSubgraphParameterFromGUID(parameterGUID);

			if (Parameter == null)
			{
				Debug.Log("Property \"" + parameterGUID + "\" Can't be found !");

				// Delete this node as the property can't be found
				graph.RemoveNode(this);
			}
		}

		void OnParamChanged(SubgraphParameter modifiedParam)
		{
			if (Parameter == modifiedParam)
			{
				onParameterChanged?.Invoke();
			}
		}

		[CustomPortBehavior(nameof(output))]
		IEnumerable<PortData> GetOutputPort(List<SerializableEdge> edges)
		{
			if (Parameter.Direction == ParameterDirection.Input)
			{
				yield return new PortData
				{
					identifier = "output",
					displayName = "Value",
					displayType = Parameter.GetValueType(),
					acceptMultipleEdges = true,
					required = true
				};
			}
		}

		[CustomPortBehavior(nameof(input))]
		IEnumerable<PortData> GetInputPort(List<SerializableEdge> edges)
		{
			if (Parameter.Direction == ParameterDirection.Output)
			{
				yield return new PortData
				{
					identifier = "input",
					displayName = "Value",
					displayType = Parameter.GetValueType(),
					required = true
				};
			}
		}

		protected override void Process() => throw new NotImplementedException("Parameters should be expanded when a SubGraph is instanced.");
	}
}

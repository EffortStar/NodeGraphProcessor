using System;
using System.Collections.Generic;
using UnityEngine;

namespace GraphProcessor
{
	[Serializable]
	public sealed class ParameterNode : BaseNode
	{
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
			Parameter = graph.GetSubgraphParameterFromGuid(parameterGUID);

			if (Parameter == null)
			{
				Debug.Log("Property \"" + parameterGUID + "\" Can't be found !");

				// Delete this node as the property can't be found
				graph.RemoveNode(this);
			}
		}

		private void OnParamChanged(SubgraphParameter modifiedParam)
		{
			if (Parameter == modifiedParam)
			{
				onParameterChanged?.Invoke();
			}
		}

		[CustomPortBehavior]
		private IEnumerable<PortData> GetOutputPort()
		{
			if (Parameter == null)
				yield break;  // No port info is provided during any time when the graph isn't provided.
			
			if (Parameter!.Direction == ParameterDirection.Input)
			{
				yield return new PortData
				{
					Path = "output",
					Identifier = "output",
#if UNITY_EDITOR
					EditorOnly = new EditorOnlyPortInfo(Graph?.GetSubgraphParameterFromGuid(parameterGUID).Name ?? "Value", null, EditorOnlyPortInfo.FieldFlags.None),
#endif
					DisplayType = Parameter.GetValueType(),
					AllowMultipleEdges = true,
					IsRequired = true,
					IsInput = false
				};
			}
		}

		[CustomPortBehavior]
		private IEnumerable<PortData> GetInputPort()
		{
			if (Parameter == null)
				yield break; // No port info is provided during any time when the graph isn't provided.
			
			if (Parameter!.Direction == ParameterDirection.Output)
			{
				yield return new PortData
				{
					Path = "input",
					Identifier = "input",
#if UNITY_EDITOR
					EditorOnly = new EditorOnlyPortInfo(Graph?.GetSubgraphParameterFromGuid(parameterGUID).Name ?? "Value", null, EditorOnlyPortInfo.FieldFlags.None),
#endif
					DisplayType = Parameter.GetValueType(),
					IsRequired = true,
					IsInput = true
				};
			}
		}

		protected override void Process() => throw new NotImplementedException("Parameters should be expanded when a SubGraph is instanced.");
	}
}

using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace GraphProcessor
{
	/// <summary>
	/// Used solely as a non-generic anchor for the view.
	/// </summary>
	[Serializable]
	public abstract class SubgraphNodeBase : BaseNode
	{
		public abstract BaseGraph Subgraph { get; }
	}

	[Serializable]
	public abstract class SubgraphNodeBase<T> : SubgraphNodeBase where T : BaseGraph
	{
#if UNITY_EDITOR
		public override string name
		{
			get
			{
				if (Subgraph == null)
					return "Subgraph";
				string result = Subgraph.name;
				if (result.EndsWith("Subgraph", StringComparison.OrdinalIgnoreCase))
					result = result[..^"Subgraph".Length];
				else if (result.StartsWith("Subgraph", StringComparison.OrdinalIgnoreCase))
					result = result["Subgraph".Length..];
				return ObjectNames.NicifyVariableName(result);
			}
		}
#else
		public override string name => Subgraph == null ? "Subgraph" : Subgraph.name;
#endif

		[HideInInspector]
		[SerializeField] protected T _subgraph;

		public sealed override BaseGraph Subgraph => _subgraph;

		[Input, RequiredPort]
		public object Inputs;

		[Output]
		public object Outputs;

		[CustomPortBehavior(nameof(Inputs))]
		public IEnumerable<PortData> InputPorts(List<SerializableEdge> edges)
		{
			if (Subgraph == null)
			{
				yield break;
			}

			foreach (SubgraphParameter parameter in Subgraph.SubgraphParameters)
			{
				if (parameter.Direction != ParameterDirection.Input) continue;
				yield return new PortData
				{
					displayType = parameter.GetValueType(),
					acceptMultipleEdges = true,
					required = true,
					displayName = parameter.Name,
					identifier = parameter.Guid
				};
			}
		}

		[CustomPortBehavior(nameof(Outputs))]
		public IEnumerable<PortData> OutputPorts(List<SerializableEdge> edges)
		{
			if (Subgraph == null)
			{
				yield break;
			}

			// TODO detect connected inner nodes and also detect whether the connected outputs are marked as required.
			foreach (SubgraphParameter parameter in Subgraph.SubgraphParameters)
			{
				if (parameter.Direction != ParameterDirection.Output) continue;
				yield return new PortData
				{
					displayType = parameter.GetValueType(),
					acceptMultipleEdges = true,
					required = true,
					displayName = parameter.Name,
					identifier = parameter.Guid
				};
			}
		}
	}
}
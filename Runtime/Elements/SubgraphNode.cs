using System;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Pool;

namespace GraphProcessor
{
	/// <summary>
	/// Used solely as a non-generic anchor for the view.
	/// </summary>
	[Serializable]
	public abstract class SubgraphNodeBase : BaseNode
	{
		public abstract BaseGraph Subgraph { get; }
#if UNITY_EDITOR
		protected static Stack<BaseNode> s_stack = new();
#endif
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

			// Collect all the parameter nodes.
			using var _ = DictionaryPool<SubgraphParameter, List<ParameterNode>>.Get(out var parametersToNodes);
			foreach (ParameterNode node in Subgraph.nodes.OfType<ParameterNode>())
			{
				// Must get from the subgraph, not the node. Because the node hasn't been initialized via the view.
				SubgraphParameter parameter = Subgraph.GetSubgraphParameterFromGUID(node.parameterGUID);
				if (parameter.Direction != ParameterDirection.Input) continue;

				if (!parametersToNodes.TryGetValue(parameter, out List<ParameterNode> list))
					parametersToNodes.Add(parameter, list = new List<ParameterNode>());
				list.Add(node);
			}

			// Generate the input ports.
			foreach (SubgraphParameter parameter in Subgraph.SubgraphParameters)
			{
				if (parameter.Direction != ParameterDirection.Input) continue;
				(bool required, bool acceptMultipleEdges) = GetParameterPortInfoFromInner(parametersToNodes, parameter);
				yield return new PortData
				{
					displayType = parameter.GetValueType(),
					acceptMultipleEdges = acceptMultipleEdges,
					required = required,
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

			// Collect all the parameter nodes.
			using var _ = DictionaryPool<SubgraphParameter, List<ParameterNode>>.Get(out var parametersToNodes);
			foreach (ParameterNode node in Subgraph.nodes.OfType<ParameterNode>())
			{
				// Must get from the subgraph, not the node. Because the node hasn't been initialized via the view.
				SubgraphParameter parameter = Subgraph.GetSubgraphParameterFromGUID(node.parameterGUID);
				if (parameter.Direction != ParameterDirection.Output) continue;

				if (!parametersToNodes.TryGetValue(parameter, out List<ParameterNode> list))
					parametersToNodes.Add(parameter, list = new List<ParameterNode>());
				list.Add(node);
			}

			// Generate the output ports.
			foreach (SubgraphParameter parameter in Subgraph.SubgraphParameters)
			{
				if (parameter.Direction != ParameterDirection.Output) continue;
				(bool required, bool acceptMultipleEdges) = GetParameterPortInfoFromInner(parametersToNodes, parameter);
				yield return new PortData
				{
					displayType = parameter.GetValueType(),
					acceptMultipleEdges = acceptMultipleEdges,
					required = required,
					displayName = parameter.Name,
					identifier = parameter.Guid
				};
			}
		}

		protected override void Process()
			=> throw new NotSupportedException($"{this} attempted execution. Call {nameof(BaseGraph)}.{nameof(BaseGraph.Realise)} to inline subgraph nodes before processing.");

		private (bool required, bool acceptMultipleEdges) GetParameterPortInfoFromInner(
			Dictionary<SubgraphParameter, List<ParameterNode>> parametersToNodes,
			SubgraphParameter parameter
		)
		{
			if (!parametersToNodes.TryGetValue(parameter, out List<ParameterNode> nodes))
			{
				AddMessage("A Subgraph Parameter is missing a matching node and must be repaired.", BadgeMessageType.Error);
				return (false, false);
			}
			var required = false;
			var acceptMultipleEdges = false;
			
#if UNITY_EDITOR
			s_stack.Clear();
			foreach (ParameterNode parameterNode in nodes)
			{
				s_stack.Push(parameterNode);
			}

			// Walk the edges to find info about the parameters.
			while (s_stack.TryPop(out BaseNode node))
			{
				if (parameter.Direction == ParameterDirection.Input)
				{
					// Walk through nodes and edges towards node input ports
					foreach (NodePort port in node.outputPorts)
					{
						foreach (SerializableEdge edge in port.GetEdges())
						{
							if (edge.inputNode is SimplifiedRelayNode)
							{
								s_stack.Push(edge.inputNode);
							}
							else
							{
								required |= edge.inputPort.portData.required;
								acceptMultipleEdges |= edge.inputPort.portData.acceptMultipleEdges;
							}
						}
					}
				}
				else
				{
					// Walk through nodes and edges towards node output ports
					foreach (NodePort port in node.inputPorts)
					{
						foreach (SerializableEdge edge in port.GetEdges())
						{
							if (edge.outputNode is SimplifiedRelayNode)
							{
								s_stack.Push(edge.outputNode);
							}
							else
							{
								required |= edge.outputPort.portData.required;
								acceptMultipleEdges |= edge.outputPort.portData.acceptMultipleEdges;
							}
						}
					}
				}
			}
#endif

			return (required, acceptMultipleEdges);
		}
	}
}
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
	[Serializable]
	public sealed class SubgraphNode : BaseNode
	{
#if UNITY_EDITOR
		private static Stack<BaseNode> s_stack = new();
		
		public static string GetNameFromSubgraph(BaseGraph graph)
		{
			if (graph == null)
				return "Subgraph";
			string result = graph.name;
			if (result.EndsWith("Subgraph", StringComparison.OrdinalIgnoreCase))
				result = result[..^"Subgraph".Length];
			else if (result.StartsWith("Subgraph", StringComparison.OrdinalIgnoreCase))
				result = result["Subgraph".Length..];
			return ObjectNames.NicifyVariableName(result);
		}
#endif
		
#if UNITY_EDITOR
		public override string name => GetNameFromSubgraph(Subgraph);
#else
		public override string name => Subgraph == null ? "Subgraph" : Subgraph.name;
#endif

		protected override void Enable()
		{
			if (_subgraph == null)
				return;
			// Force the subgraph to initialize before this node does.
			// If the subgraph isn't enabled first then its nodes and ports won't have valid data when queried.
			if (!_subgraph.isEnabled)
				_subgraph.OnEnable();
		}

		[HideInInspector]
		[SerializeField] private BaseGraph _subgraph;

		public BaseGraph Subgraph
		{
			get => _subgraph;
			internal set => _subgraph = value;
		}

		[Input, RequiredPort]
		public object Inputs;

		[Output]
		public object Outputs;

		[CustomPortBehavior(nameof(Inputs))]
		public IEnumerable<PortData> InputPorts()
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
				SubgraphParameter parameter = Subgraph.GetSubgraphParameterFromGuid(node.parameterGUID);
				if (parameter.Direction != ParameterDirection.Input) continue;

				if (!parametersToNodes.TryGetValue(parameter, out List<ParameterNode> list))
					parametersToNodes.Add(parameter, list = new List<ParameterNode>());
				list.Add(node);
			}

			// Generate the input ports.
			foreach (SubgraphParameter parameter in Subgraph.SubgraphParameters)
			{
				if (parameter.Direction != ParameterDirection.Input) continue;
				(bool _, bool acceptMultipleEdges
#if UNITY_EDITOR
						, EditorOnlyPortInfo editorOnly
#endif
					) = GetParameterPortInfoFromInner(parametersToNodes, parameter);


				Type t = parameter.GetValueType();
				bool isNullable = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>);
				yield return new PortData
				{
					displayType = t,
					acceptMultipleEdges = acceptMultipleEdges,
					// Input ports for subgraphs are always required because their edges are connected.
					// Unless that type is nullable, then we know it doesn't need to be assigned.
					// Note that when modifying this logic, a subgraph input port could connect to multiple ports.
					required = !isNullable,
					identifier = parameter.Guid,
#if UNITY_EDITOR
					EditorOnly = new EditorOnlyPortInfo(parameter.Name, editorOnly.Tooltip, editorOnly.Flags)
#endif
				};
			}
		}

		[CustomPortBehavior(nameof(Outputs))]
		public IEnumerable<PortData> OutputPorts()
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
				SubgraphParameter parameter = Subgraph.GetSubgraphParameterFromGuid(node.parameterGUID);
				if (parameter.Direction != ParameterDirection.Output) continue;

				if (!parametersToNodes.TryGetValue(parameter, out List<ParameterNode> list))
					parametersToNodes.Add(parameter, list = new List<ParameterNode>());
				list.Add(node);
			}

			// Generate the output ports.
			foreach (SubgraphParameter parameter in Subgraph.SubgraphParameters)
			{
				if (parameter.Direction != ParameterDirection.Output) continue;
				(bool required, bool acceptMultipleEdges
#if UNITY_EDITOR
						, EditorOnlyPortInfo editorOnly
#endif
					) = GetParameterPortInfoFromInner(parametersToNodes, parameter);
				yield return new PortData
				{
					displayType = parameter.GetValueType(),
					acceptMultipleEdges = acceptMultipleEdges,
					required = required,
					identifier = parameter.Guid,
#if UNITY_EDITOR
					EditorOnly = new EditorOnlyPortInfo(parameter.Name, editorOnly.Tooltip, editorOnly.Flags)
#endif
				};
			}
		}

		protected override void Process()
			=> throw new NotSupportedException($"{this} attempted execution. Call {nameof(BaseGraph)}.{nameof(BaseGraph.Realize)} to inline subgraph nodes before processing.");

		private (
			bool required, bool 
			acceptMultipleEdges
#if UNITY_EDITOR
			, EditorOnlyPortInfo editorOnly
#endif
			) GetParameterPortInfoFromInner(
			Dictionary<SubgraphParameter, List<ParameterNode>> parametersToNodes,
			SubgraphParameter parameter
		)
		{
			if (!parametersToNodes.TryGetValue(parameter, out List<ParameterNode> nodes))
			{
				AddMessage("A Subgraph Parameter is missing a matching node and must be repaired.", BadgeMessageType.Error);
				return (false, false
#if UNITY_EDITOR
						, editorOnly: default
#endif
					);
			}
			var required = false;
			
#if UNITY_EDITOR
			var acceptMultipleEdges = false;
			EditorOnlyPortInfo? editorOnly = null;
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
						foreach (SerializableEdge edge in port.Edges)
						{
							if (edge.ToNode is SimplifiedRelayNode)
							{
								s_stack.Push(edge.ToNode);
							}
							else
							{
								if (edge.ToPort.Edges.Count <= 1) // Edges are only required if what's querying it is all that's connected.
									required |= edge.ToPort.portData.required;
								acceptMultipleEdges |= edge.ToPort.portData.acceptMultipleEdges;
								editorOnly ??= edge.ToPort.portData.EditorOnly;
							}
						}
					}
				}
				else
				{
					// Walk through nodes and edges towards node output ports
					foreach (NodePort port in node.inputPorts)
					{
						foreach (SerializableEdge edge in port.Edges)
						{
							if (edge.FromNode is SimplifiedRelayNode)
							{
								s_stack.Push(edge.FromNode);
							}
							else
							{
								if (edge.FromPort.Edges.Count <= 1) // Edges are only required if what's querying it is all that's connected.
									required |= edge.FromPort.portData.required;
								acceptMultipleEdges |= edge.FromPort.portData.acceptMultipleEdges;
								editorOnly ??= edge.FromPort.portData.EditorOnly;
							}
						}
					}
				}
			}
#else
			// Never clean up SubgraphNode in builds.
			var acceptMultipleEdges = true;
#endif

			return (required, acceptMultipleEdges
#if UNITY_EDITOR
					, editorOnly ?? default
#endif
				);
		}
	}
}
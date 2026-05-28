using System;
using System.Collections.Generic;
using UnityEngine;

namespace GraphProcessor
{
	[Serializable]
	public sealed class SerializableEdge : ISerializationCallbackReceiver
	{
		public string GUID;
		[SerializeField] internal string inputNodeGUID;
		[SerializeField] internal string outputNodeGUID;

		public string FromNodeGuid => outputNodeGUID;
		public string ToNodeGuid => inputNodeGUID;

		/// <summary>
		/// Formerly InputNode
		/// </summary>
		[NonSerialized] public BaseNode ToNode;

		/// <summary>
		/// Formerly InputPort
		/// </summary>
		[NonSerialized] public NodePort ToPort; // edge goes from output to input

		/// <summary>
		/// Formerly OutputPort
		/// </summary>
		[NonSerialized] public NodePort FromPort;

		//temporary object used to send port to port data when a custom input/output function is used.
		[NonSerialized] public object PassThroughBuffer;

		/// <summary>
		/// Formerly OutputNode
		/// </summary>
		[NonSerialized] public BaseNode FromNode;

		public string inputFieldName;
		public string outputFieldName;

		// Use to store the id of the field that generate multiple ports
		public string inputPortIdentifier;
		public string outputPortIdentifier;

		public static SerializableEdge CreateNewEdge(BaseGraph graph, NodePort fromPort, NodePort toPort)
		{
			return new SerializableEdge
			{
				GUID = Guid.NewGuid().ToString(),
				ToNode = toPort.owner,
				inputFieldName = toPort.fieldName,
				FromNode = fromPort.owner,
				outputFieldName = fromPort.fieldName,
				ToPort = toPort,
				FromPort = fromPort,
				inputPortIdentifier = toPort.portData.identifier,
				outputPortIdentifier = fromPort.portData.identifier
			};
		}

		public void OnBeforeSerialize()
		{
			outputNodeGUID = FromNode?.GUID;
			inputNodeGUID = ToNode?.GUID;
		}

		public void OnAfterDeserialize()
		{
		}

		public enum DeserializationResult
		{
			NoChanges,
			Changed
		}

		//here our owner have been deserialized
		public DeserializationResult Deserialize(BaseGraph graph, bool logWarnings = true)
		{
			if (!graph.nodesPerGUID.ContainsKey(outputNodeGUID) || !graph.nodesPerGUID.ContainsKey(inputNodeGUID))
			{
				if (logWarnings)
					Debug.LogWarning($"[NodeGraph] Edge {GUID} failed to deserialize due to invalid node GUIDs ({inputNodeGUID} -> {outputNodeGUID}, owner: {graph})", graph);

				return DeserializationResult.NoChanges;
			}

			FromNode = graph.nodesPerGUID[outputNodeGUID];
			ToNode = graph.nodesPerGUID[inputNodeGUID];
			ToPort = ToNode.GetPort(inputFieldName, inputPortIdentifier);
			FromPort = FromNode.GetPort(outputFieldName, outputPortIdentifier);

			var result = DeserializationResult.NoChanges;
			if (ToPort == null)
			{
				if (ToNode.TryGetFallbackPort(ref inputFieldName, ref inputPortIdentifier, out ToPort))
				{
					result = DeserializationResult.Changed;
				}
				else
				{
					if (logWarnings)
						Debug.LogWarning($"[NodeGraph] Edge {GUID} failed to deserialize due to invalid input port (fieldName: {inputFieldName}, id: {inputPortIdentifier}, owner: {graph})", graph);
				}
			}
			else
			{
#if UNITY_EDITOR
				if ((ToPort.portData.EditorOnly.Flags & EditorOnlyPortInfo.FieldFlags.Obsolete) != 0)
				{
					Debug.LogError($"[NodeGraph] Edge was connected to Obsolete port {ToPort.portData.EditorOnly.DisplayName} on {ToNode}.", graph);
				}
#endif
			}

			if (FromPort == null)
			{
				if (FromNode.TryGetFallbackPort(ref outputFieldName, ref outputPortIdentifier, out FromPort))
				{
					result = DeserializationResult.Changed;
				}
				else
				{
					if (logWarnings)
						Debug.LogWarning($"[NodeGraph] Edge {GUID} failed to deserialize due to invalid output port (fieldName: {outputFieldName}, id: {outputPortIdentifier}, owner: {graph})", graph);
				}
			}
			else
			{
#if UNITY_EDITOR
				if ((FromPort.portData.EditorOnly.Flags & EditorOnlyPortInfo.FieldFlags.Obsolete) != 0)
				{
					Debug.LogError($"[NodeGraph] Edge was connected to Obsolete port {FromPort.portData.EditorOnly.DisplayName} on {FromNode}.", graph);
				}
#endif
			}

			return result;
		}

		public void RemapNodes(BaseGraph graph, Dictionary<string, BaseNode> map)
		{
			var reserialize = false;
			if (map.TryGetValue(ToNodeGuid, out BaseNode toNode))
			{
				inputNodeGUID = toNode.GUID;
				reserialize = true;
			}

			if (map.TryGetValue(FromNodeGuid, out BaseNode fromNode))
			{
				outputNodeGUID = fromNode.GUID;
				reserialize = true;
			}

			if (!reserialize)
				return;
			Deserialize(graph, false);
		}

		public override string ToString()
			=> $"{FromNode?.name ?? FromNodeGuid}:{FromPort?.fieldName ?? outputFieldName}"
#if UNITY_EDITOR
				+ $" ({FromPort?.portData.EditorOnly.DisplayName})"
#endif
				+ $" -> {ToNode?.name ?? ToNodeGuid}:{ToPort?.fieldName ?? inputFieldName}"
#if UNITY_EDITOR
				+ $" ({ToPort?.portData.EditorOnly.DisplayName})"
#endif
		;
	}
}
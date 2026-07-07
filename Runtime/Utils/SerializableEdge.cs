using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Serialization;
using static GraphProcessor.GraphExpressionCompilation;

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

		/// <summary>
		/// Formerly OutputNode
		/// </summary>
		[NonSerialized] public BaseNode FromNode;

		[FormerlySerializedAs("inputFieldName")]
		public string InputFieldPath;

		[FormerlySerializedAs("outputFieldName")]
		public string OutputFieldPath;

		// Use to store the id of the field that generate multiple ports
		public string inputPortIdentifier;
		public string outputPortIdentifier;

		public string Key
		{
			get
			{
				string fromKey = FromPort.Key;
				string toKey = ToPort.Key;
				if (fromKey == null || toKey == null)
				{
					return null;
				}
				
				return $"{fromKey} To {toKey}";
			}
		}

		// Cached to prevent re-hashing all this info every time we push edge data.
		[NonSerialized]
		[CanBeNull] private PushDataDelegate _pushDataDelegate;

		public static SerializableEdge CreateNewEdge(
			NodePort fromPort,
			NodePort toPort
		)
		{
			return new SerializableEdge
			{
				GUID = Guid.NewGuid().ToString(),
				ToNode = toPort.Owner,
				InputFieldPath = toPort.FieldPath,
				FromNode = fromPort.Owner,
				OutputFieldPath = fromPort.FieldPath,
				ToPort = toPort,
				FromPort = fromPort,
				inputPortIdentifier = toPort.Identifier,
				outputPortIdentifier = fromPort.Identifier
			};
		}

		public void OnBeforeSerialize()
		{
			outputNodeGUID = FromNode?.GUID;
			inputNodeGUID = ToNode?.GUID;
		}

		public void OnAfterDeserialize() { }

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
			ToPort = ToNode.GetPort(InputFieldPath, inputPortIdentifier);
			FromPort = FromNode.GetPort(OutputFieldPath, outputPortIdentifier);

			var result = DeserializationResult.NoChanges;
			if (ToPort == null)
			{
				if (ToNode.TryGetFallbackPort(ref InputFieldPath, ref inputPortIdentifier, out ToPort))
				{
					result = DeserializationResult.Changed;
				}
				else
				{
					if (logWarnings)
						Debug.LogWarning($"[NodeGraph] Edge {GUID} failed to deserialize due to invalid input port (fieldName: {InputFieldPath}, id: {inputPortIdentifier}, owner: {graph})", graph);
				}
			}
			else
			{
#if UNITY_EDITOR
				if ((ToPort.EditorOnly.Flags & EditorOnlyPortInfo.FieldFlags.Obsolete) != 0)
				{
					Debug.LogError($"[NodeGraph] Edge was connected to Obsolete port {ToPort.EditorDisplayName} on {ToNode}.", graph);
				}
#endif
			}

			if (FromPort == null)
			{
				if (FromNode.TryGetFallbackPort(ref OutputFieldPath, ref outputPortIdentifier, out FromPort))
				{
					result = DeserializationResult.Changed;
				}
				else
				{
					if (logWarnings)
						Debug.LogWarning($"[NodeGraph] Edge {GUID} failed to deserialize due to invalid output port (fieldName: {OutputFieldPath}, id: {outputPortIdentifier}, owner: {graph})", graph);
				}
			}
			else
			{
#if UNITY_EDITOR
				if ((FromPort.EditorOnly.Flags & EditorOnlyPortInfo.FieldFlags.Obsolete) != 0)
				{
					Debug.LogError($"[NodeGraph] Edge was connected to Obsolete port {FromPort.EditorDisplayName} on {FromNode}.", graph);
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
		
		public void PushData()
		{
			_pushDataDelegate ??= GetPushDataDelegate(this, FromNode.Graph);
			_pushDataDelegate?.Invoke(FromNode, ToNode);
		}

		public override string ToString()
			=> $"{FromNode?.name ?? FromNodeGuid}:{FromPort?.FieldPath ?? OutputFieldPath}{(string.IsNullOrEmpty(outputPortIdentifier) ? "" : $"({outputPortIdentifier})")}"
#if UNITY_EDITOR
				+ $" ({FromPort?.EditorDisplayName})"
#endif
				+ $" -> {ToNode?.name ?? ToNodeGuid}:{ToPort?.FieldPath ?? InputFieldPath}{(string.IsNullOrEmpty(inputPortIdentifier) ? "" : $"({inputPortIdentifier})")}"
#if UNITY_EDITOR
				+ $" ({ToPort?.EditorDisplayName})"
#endif
		;
	}
}
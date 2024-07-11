using System;
using System.Linq;
using UnityEngine;

namespace GraphProcessor
{
	[Serializable]
	public sealed class SerializableEdge : ISerializationCallbackReceiver
	{
		public string GUID;

		[SerializeField] BaseGraph owner;

		[SerializeField] string inputNodeGUID;
		[SerializeField] string outputNodeGUID;

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
				owner = graph,
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
		public DeserializationResult Deserialize()
		{
			if (!owner.nodesPerGUID.ContainsKey(outputNodeGUID) || !owner.nodesPerGUID.ContainsKey(inputNodeGUID))
			{
				Debug.LogWarning($"Edge {GUID} failed to deserialize due to invalid node GUIDs ({inputNodeGUID} -> {outputNodeGUID})");
				return DeserializationResult.NoChanges;
			}

			FromNode = owner.nodesPerGUID[outputNodeGUID];
			ToNode = owner.nodesPerGUID[inputNodeGUID];
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
					Debug.LogWarning($"Edge {GUID} failed to deserialize due to invalid input port (fieldName: {inputFieldName}, id: {inputPortIdentifier})");
				}
			}

			if (FromPort == null)
			{
				if (FromNode.TryGetFallbackPort(ref outputFieldName, ref outputPortIdentifier, out FromPort))
				{
					result = DeserializationResult.Changed;
				}
				else
				{
					Debug.LogWarning($"Edge {GUID} failed to deserialize due to invalid output port (fieldName: {outputFieldName}, id: {outputPortIdentifier})");
				}
			}

			return result;
		}

		public override string ToString() => $"{FromNode.name}:{FromPort.fieldName} -> {ToNode.name}:{ToPort.fieldName}";
	}
}
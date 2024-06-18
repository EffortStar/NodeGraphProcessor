using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Serialization;

namespace GraphProcessor
{
	[Serializable]
	public sealed class SerializableEdge : ISerializationCallbackReceiver
	{
		public string GUID;

		[SerializeField] BaseGraph owner;

		[SerializeField] string inputNodeGUID;
		[SerializeField] string outputNodeGUID;

		[NonSerialized] public BaseNode inputNode;

		[NonSerialized] public NodePort inputPort;
		[NonSerialized] public NodePort outputPort;

		//temporary object used to send port to port data when a custom input/output function is used.
		[NonSerialized] public object passThroughBuffer;

		[NonSerialized] public BaseNode outputNode;

		public string inputFieldName;
		public string outputFieldName;

		// Use to store the id of the field that generate multiple ports
		public string inputPortIdentifier;
		public string outputPortIdentifier;

		public static SerializableEdge CreateNewEdge(BaseGraph graph, NodePort inputPort, NodePort outputPort)
		{
			return new SerializableEdge
			{
				owner = graph,
				GUID = Guid.NewGuid().ToString(),
				inputNode = inputPort.owner,
				inputFieldName = inputPort.fieldName,
				outputNode = outputPort.owner,
				outputFieldName = outputPort.fieldName,
				inputPort = inputPort,
				outputPort = outputPort,
				inputPortIdentifier = inputPort.portData.identifier,
				outputPortIdentifier = outputPort.portData.identifier
			};
		}

		public void OnBeforeSerialize()
		{
			outputNodeGUID = outputNode?.GUID;
			inputNodeGUID = inputNode?.GUID;
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

			outputNode = owner.nodesPerGUID[outputNodeGUID];
			inputNode = owner.nodesPerGUID[inputNodeGUID];
			inputPort = inputNode.GetPort(inputFieldName, inputPortIdentifier);
			outputPort = outputNode.GetPort(outputFieldName, outputPortIdentifier);

			var result = DeserializationResult.NoChanges;
			if (inputPort == null)
			{
				if (TryGetFallbackPort(inputNode, inputFieldName, out inputPort))
				{
					inputFieldName = inputPort.fieldName;
					result = DeserializationResult.Changed;
				}
				else
				{
					Debug.LogWarning($"Edge {GUID} failed to deserialize due to invalid input port (fieldName: {inputFieldName}, id: {inputPortIdentifier})");
				}
			}

			if (outputPort == null)
			{
				if (TryGetFallbackPort(outputNode, outputFieldName, out outputPort))
				{
					outputFieldName = outputPort.fieldName;
					result = DeserializationResult.Changed;
				}
				else
				{

					Debug.LogWarning($"Edge {GUID} failed to deserialize due to invalid output port (fieldName: {outputFieldName}, id: {outputPortIdentifier})");
				}
			}

			return result;

			bool TryGetFallbackPort(BaseNode node, string fieldName, out NodePort value)
			{
				foreach (NodePort port in node.AllPorts)
				{
					foreach (FormerlySerializedAsAttribute attribute in port.fieldInfo.GetCustomAttributes<FormerlySerializedAsAttribute>())
					{
						if (attribute.oldName != fieldName) continue;
						value = port;
						return true;
					}
				}

				value = null;
				return false;
			}
		}

		public override string ToString() => $"{outputNode.name}:{outputPort.fieldName} -> {inputNode.name}:{inputPort.fieldName}";
	}
}
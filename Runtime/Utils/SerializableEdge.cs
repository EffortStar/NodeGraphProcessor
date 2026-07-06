using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;
using UnityEngine.Serialization;

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

		[FormerlySerializedAs("inputFieldName")] public string InputFieldPath;
		[FormerlySerializedAs("outputFieldName")] public string OutputFieldPath;

		// Use to store the id of the field that generate multiple ports
		public string inputPortIdentifier;
		public string outputPortIdentifier;

		// Cached to prevent re-hashing all this info every time we push edge data.
		[NonSerialized]
		private EdgeKey? _key;

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
		
		private static readonly Dictionary<EdgeKey, PushDataDelegate> s_pushDataDelegates = new();

		private readonly struct EdgeKey : IEquatable<EdgeKey>
		{
			private readonly FieldInfo _fromRoot;
			private readonly FieldInfo _fromLeaf;
			private readonly FieldInfo _toRoot;
			private readonly FieldInfo _toLeaf;
			private readonly int _hashCode;

			public EdgeKey(
				NodeFieldInformation from,
				NodeFieldInformation to
			)
			{
				_fromRoot = from.Path.Root.FieldInfo;
				_toRoot = to.Path.Root.FieldInfo;
				_fromLeaf = from.Path.FieldInfo;
				_toLeaf = to.Path.FieldInfo;
				_hashCode = HashCode.Combine(_fromRoot, _toRoot, _fromLeaf, _toLeaf);
			}

			public bool Equals(EdgeKey other) =>
				_fromRoot.Equals(other._fromRoot)
				&& _toRoot.Equals(other._toRoot)
				&& _fromLeaf.Equals(other._fromLeaf)
				&& _toLeaf.Equals(other._toLeaf);

			public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);

			public override int GetHashCode() => _hashCode;

			public static bool operator ==(EdgeKey left, EdgeKey right) => left.Equals(right);

			public static bool operator !=(EdgeKey left, EdgeKey right) => !left.Equals(right);
		}
		
		public void PushData()
		{
			if (GetPushDataDelegate(out PushDataDelegate edgeDelegate))
			{
				edgeDelegate(FromNode, ToNode);
			}
		}
		
		
		/// <summary>
		/// Delegate to send the data from this port to another port connected by an edge.
		/// </summary>
		private delegate void PushDataDelegate(BaseNode from, BaseNode to);

		private bool GetPushDataDelegate(out PushDataDelegate edgeDelegate)
		{
			if (_key is not { } edgeKey)
			{

				if (FromPort._fieldInfo == null || ToPort._fieldInfo == null)
				{
					Debug.LogError($"[NodeGraph] Edges generated with {nameof(CustomPortBehaviorAttribute)} cannot be executed and must be removed from the graph during Realization. ({this})");
					edgeDelegate = null;
					return false;
				}

				_key = edgeKey = new EdgeKey(
					FromPort._fieldInfo,
					ToPort._fieldInfo
				);
			}

			if (s_pushDataDelegates.TryGetValue(edgeKey, out edgeDelegate))
			{
				return true;
			}

			edgeDelegate = CreatePushDataDelegateForEdge(this);

			if (edgeDelegate == null)
			{
				return false;
			}

			s_pushDataDelegates.Add(edgeKey, edgeDelegate);
			return true;
		}
		
		private static readonly ParameterExpression[] s_params = new ParameterExpression[2];

		private static PushDataDelegate CreatePushDataDelegateForEdge(SerializableEdge edge)
		{
			try
			{
				NodeFieldInformation fromFieldInfo = edge.FromPort._fieldInfo;
				NodeFieldInformation toFieldInfo = edge.ToPort._fieldInfo;

				// We keep slow checks inside the editor
#if UNITY_EDITOR
				if (!BaseGraph.TypesAreConnectable(fromFieldInfo.FieldType, toFieldInfo.FieldType))
				{
					Debug.LogError($"[NodeGraph] Can't convert from {fromFieldInfo.FieldType} to {toFieldInfo.FieldType}, " +
						"you must specify a custom port function (i.e CustomPortInput or CustomPortOutput) for non-implicit conversions. " +
						$" {edge.FromNode} -> {edge.ToNode}"
					);
					return null;
				}
#endif

				if (toFieldInfo.Path.Parent != null)
				{
					// NOTE: This says "OutputObjectAttribute", because there isn't an InputObjectAttribute yet.
					Debug.LogError($"[NodeProcessor] {nameof(OutputObjectAttribute)} is not yet supported on input ports.");
					return null;
				}

				// Take the node in as a parameter.
				ParameterExpression fromParam = Expression.Parameter(typeof(BaseNode), "from");
				ParameterExpression toParam = Expression.Parameter(typeof(BaseNode), "to");
				s_params[0] = fromParam;
				s_params[1] = toParam;

				// Convert the parameter to their real types.
				NodeFieldPath fromLeaf = fromFieldInfo.Path;
				NodeFieldPath fromRoot = fromLeaf.Root;
				UnaryExpression fromConverted = Expression.Convert(fromParam, fromRoot.FieldInfo.DeclaringType!);

				Expression fromParamField;
				if (fromLeaf != fromRoot)
				{
					Stack<NodeFieldPath> pathStack = new();
					for (NodeFieldPath from = fromLeaf; from != null; from = from.Parent)
					{
						pathStack.Push(from);
					}

					fromParamField = fromConverted;
					while (pathStack.TryPop(out NodeFieldPath path))
					{
						fromParamField = Expression.Field(fromParamField, path.FieldInfo);
					}
				}
				else
				{
					fromParamField = Expression.Field(fromConverted, fromLeaf.FieldInfo);
				}

				Type fromType = edge.FromPort.DisplayType ?? fromFieldInfo.FieldType;

				UnaryExpression toConverted = Expression.Convert(toParam, toFieldInfo.Path.FieldInfo.DeclaringType!);
				Expression toParamField = Expression.Field(toConverted, toFieldInfo.Path.FieldInfo);
				Type toType = edge.ToPort.DisplayType ?? toFieldInfo.FieldType;

				if (fromType != toType)
				{
					// If there is a user defined conversion function, then we call it
					if (TypeAdapter.AreAssignable(fromType, toType))
					{
						// We add a cast in case there we're calling the conversion method with a base class parameter (like object)
						UnaryExpression convertedParam = Expression.Convert(fromParamField, fromType);
						fromParamField = Expression.Call(TypeAdapter.GetConversionMethod(fromType, toType), convertedParam);
						// In case there is a custom port behavior in the output, then we need to re-cast to the base type because
						// the conversion method return type is not always assignable directly:
						fromParamField = Expression.Convert(fromParamField, toFieldInfo.FieldType);
					}
					else // otherwise we cast
					{
						fromParamField = Expression.Convert(fromParamField, toFieldInfo.FieldType);
					}
				}

				BinaryExpression assign = Expression.Assign(toParamField, fromParamField);
				return Expression.Lambda<PushDataDelegate>(assign, s_params).Compile();
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				return null;
			}
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
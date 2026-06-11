// #define DEBUG_LAMBDA

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;
using UnityEngine;

namespace GraphProcessor
{
	/// <summary>
	/// Runtime class that stores all info about one port that is needed for the processing
	/// </summary>
	public class NodePort
	{
		/// <summary>
		/// The actual name of the property behind the port (must be exact, it is used for Reflection)
		/// </summary>
		public string FieldPath => _fieldInfo?.Path.FieldPath ?? _portData.Path;
		public string Identifier => _portData?.Identifier;
		public Type DisplayType => _fieldInfo?.FieldType ?? _portData.DisplayType;
		public bool IsRequired => _fieldInfo?.IsRequired ?? _portData.IsRequired;
		public bool IsInput => _fieldInfo?.IsInput ?? _portData.IsInput;
		public bool IsVertical => _fieldInfo?.IsVertical ?? _portData.IsVertical;
		public bool AllowMultipleEdges => _fieldInfo?.AllowMultipleEdges ?? _portData.AllowMultipleEdges;
		public bool IsCustom => _fieldInfo == null;
		[CanBeNull] public FieldInfo FieldInfo => _fieldInfo?.Path.FieldInfo;
#if UNITY_EDITOR
		public EditorOnlyPortInfo EditorOnly => _fieldInfo?.EditorOnly ?? _portData.EditorOnly;

		[CanBeNull] private string _displayNameOverride;
		public string EditorDisplayName
		{
			get => _displayNameOverride ?? EditorOnly.DisplayName;
			set => _displayNameOverride = value;
		}
#endif

		/// <summary>
		/// The node on which the port is
		/// </summary>
		public readonly BaseNode Owner;

		private readonly NodeFieldInformation _fieldInfo;
		private PortData _portData;

		private readonly List<SerializableEdge> _edges = new();
		private static readonly Dictionary<PushDataDelegateKey, PushDataDelegate> s_pushDataDelegates = new();

		private readonly struct PushDataDelegateKey : IEquatable<PushDataDelegateKey>
		{
			private readonly FieldInfo _fromRoot;
			private readonly FieldInfo _fromLeaf;
			private readonly FieldInfo _toRoot;
			private readonly FieldInfo _toLeaf;

			public PushDataDelegateKey(
				NodeFieldInformation from,
				NodeFieldInformation to
			)
			{
				_fromRoot = from.Path.Root.FieldInfo;
				_toRoot = to.Path.Root.FieldInfo;
				_fromLeaf = from.Path.FieldInfo;
				_toLeaf = to.Path.FieldInfo;
			}

			public bool Equals(PushDataDelegateKey other) =>
				_fromRoot.Equals(other._fromRoot)
				&& _toRoot.Equals(other._toRoot)
				&& _fromLeaf.Equals(other._fromLeaf)
				&& _toLeaf.Equals(other._toLeaf);

			public override bool Equals(object obj) => obj is PushDataDelegateKey other && Equals(other);

			public override int GetHashCode() => HashCode.Combine(_fromRoot, _toRoot, _fromLeaf, _toLeaf);

			public static bool operator ==(PushDataDelegateKey left, PushDataDelegateKey right) => left.Equals(right);

			public static bool operator !=(PushDataDelegateKey left, PushDataDelegateKey right) => !left.Equals(right);
		}

		/// <summary>
		/// Delegate to send the data from this port to another port connected by an edge.
		/// </summary>
		private delegate void PushDataDelegate(BaseNode from, BaseNode to);

		private static bool GetPushDataDelegate(SerializableEdge edge, out PushDataDelegate edgeDelegate)
		{
			if (edge.FromPort._fieldInfo == null || edge.ToPort._fieldInfo == null)
			{
				Debug.LogError($"[NodeGraph] Edges generated with {nameof(CustomPortBehaviorAttribute)} cannot be executed and must be removed from the graph during Realization. ({edge})");
				edgeDelegate = null;
				return false;
			}
			
			PushDataDelegateKey key = new(
				edge.FromPort._fieldInfo,
				edge.ToPort._fieldInfo
			);
			if (s_pushDataDelegates.TryGetValue(key, out edgeDelegate))
			{
				return true;
			}

			edgeDelegate = CreatePushDataDelegateForEdge(edge);

			if (edgeDelegate == null)
			{
				return false;
			}

			s_pushDataDelegates.Add(key, edgeDelegate);
			return true;
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="owner">owner node</param>
		/// <param name="nodeFieldInfo">Complete info about the field</param>
		public NodePort(BaseNode owner, NodeFieldInformation nodeFieldInfo)
		{
			Owner = owner;
			_fieldInfo = nodeFieldInfo;
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="owner">owner node</param>
		/// <param name="portData">Data of the port</param>
		public NodePort(BaseNode owner, PortData portData)
		{
			Owner = owner;
			_portData = portData;
		}
		
		/// <summary>
		/// Connect an edge to this port
		/// </summary>
		/// <param name="edge"></param>
		public void Add(SerializableEdge edge)
		{
			if (!_edges.Contains(edge))
				_edges.Add(edge);
		}

		/// <summary>
		/// Disconnect an Edge from this port
		/// </summary>
		/// <param name="edge"></param>
		public void Remove(SerializableEdge edge)
		{
			if (!_edges.Contains(edge))
				return;

			_edges.Remove(edge);
		}

		/// <summary>
		/// Get all the edges connected to this port
		/// </summary>
		/// <value></value>
		public List<SerializableEdge> Edges => _edges;

		/// <summary>
		/// Push the value of the port through the edges
		/// This method can only be called on output ports
		/// </summary>
		public void PushData()
		{
			foreach (SerializableEdge edge in _edges)
			{
				if (GetPushDataDelegate(edge, out PushDataDelegate edgeDelegate))
				{
					edgeDelegate(edge.FromNode, edge.ToNode);
				}
			}
		}

		/// <summary>
		/// Reset the value of the field to default if possible
		/// </summary>
		public void ResetToDefault()
		{
			if (_fieldInfo == null)
			{
				return;
			}
			
			// Clear lists, set classes to null and struct to default value.
			if (typeof(IList).IsAssignableFrom(_fieldInfo.FieldType))
				(_fieldInfo.GetValue(Owner) as IList)?.Clear();
			else if (_fieldInfo.FieldType.GetTypeInfo().IsClass)
				_fieldInfo.SetValue(Owner, null);
			else
			{
				try
				{
					_fieldInfo.SetValue(Owner, Activator.CreateInstance(_fieldInfo.FieldType));
				}
				catch
				{
					// Catch types that don't have any constructors
				}
			}
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
			=> $"{FieldPath} "
#if UNITY_EDITOR
				+ $"({EditorDisplayName}) "
#endif
				+ "edges:\n\t" + string.Join("\n\t", _edges.Select(e => e.ToString()));

		public void OverrideCustomData(PortData portData) => _portData = portData;
	}

	/// <summary>
	/// Container of ports and the edges connected to these ports
	/// </summary>
	public abstract class NodePortContainer : List<NodePort>
	{
		private readonly BaseNode _node;

		public NodePortContainer(BaseNode node) => _node = node;

		/// <summary>
		/// Remove an edge that is connected to one of the node in the container
		/// </summary>
		/// <param name="edge"></param>
		public void Remove(SerializableEdge edge) => ForEach(p => p.Remove(edge));

		/// <summary>
		/// Add an edge that is connected to one of the node in the container
		/// </summary>
		/// <param name="edge"></param>
		public void Add(SerializableEdge edge)
		{
			string portFieldName = edge.ToNode == _node ? edge.InputFieldPath : edge.OutputFieldPath;
			string portIdentifier = edge.ToNode == _node ? edge.inputPortIdentifier : edge.outputPortIdentifier;

			// Force empty string to null since portIdentifier is a serialized value
			if (string.IsNullOrEmpty(portIdentifier))
				portIdentifier = null;

			NodePort port = this.FirstOrDefault(p => p.FieldPath == portFieldName && p.Identifier == portIdentifier);

			if (port == null)
			{
				Debug.LogError($"[NodeGraph] The edge ({edge}) can't be connected because a port couldn't be found.");
				return;
			}

			port.Add(edge);
		}
	}

	/// <inheritdoc/>
	public class NodeInputPortContainer : NodePortContainer
	{
		public NodeInputPortContainer(BaseNode node) : base(node) { }
	}

	/// <inheritdoc/>
	public class NodeOutputPortContainer : NodePortContainer
	{
		public NodeOutputPortContainer(BaseNode node) : base(node) { }

		public void PushDatas() => ForEach(p => p.PushData());
	}
}
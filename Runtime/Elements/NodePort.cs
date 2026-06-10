// #define DEBUG_LAMBDA

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using UnityEngine;

namespace GraphProcessor
{
#if UNITY_EDITOR
	public struct EditorOnlyPortInfo : IEquatable<EditorOnlyPortInfo>
	{
		[Flags]
		public enum FieldFlags
		{
			None = 0,
			Obsolete = 1 << 0
		}
			
		/// <summary>
		/// Display name on the node
		/// </summary>
		public string DisplayName;
		public string Tooltip;
		public FieldFlags Flags;

		public EditorOnlyPortInfo(
			string displayName,
			string tooltip,
			FieldFlags flags
		)
		{
			Tooltip = tooltip;
			Flags = flags;
			DisplayName = displayName;
		}

		public EditorOnlyPortInfo(FieldInfo field)
		{
			if (Attribute.IsDefined(field, typeof(InputAttribute)) && field.GetCustomAttribute<InputAttribute>() is { name: { } inName })
			{
				DisplayName = inName;
			}
			else if (Attribute.IsDefined(field, typeof(OutputAttribute)) && field.GetCustomAttribute<OutputAttribute>() is { name: { } outName })
			{
				DisplayName = outName;
			}
			else
			{
				DisplayName = PascalToSentenceCase(field.Name);
			}

			Tooltip = Attribute.IsDefined(field, typeof(TooltipAttribute))
				? $"<b>{TypeUtility.FormatTypeName(field.FieldType)}</b>: {((TooltipAttribute)Attribute.GetCustomAttribute(field, typeof(TooltipAttribute))).tooltip}"
				: $"<b>{TypeUtility.FormatTypeName(field.FieldType)}</b>";
			
			Flags = FieldFlags.None;
			if (Attribute.IsDefined(field, typeof(ObsoleteAttribute)))
			{
				Flags |= FieldFlags.Obsolete;
			}

			return;

			static string PascalToSentenceCase(string str)
			{
				string result = Regex.Replace(str, "[a-z][A-Z]", m => $"{m.Value[0]} {m.Value[1]}");
				result = result.Replace(" Id", " ID");
				return result.Length > 2 ? $"{char.ToUpper(result[0])}{result[1..]}" : result;
			}
		}

		public bool Equals(EditorOnlyPortInfo other)
			=> DisplayName == other.DisplayName
				&& Tooltip == other.Tooltip
				&& Flags == other.Flags;

		public override bool Equals(object obj) => obj is EditorOnlyPortInfo other && Equals(other);

		public override int GetHashCode() => HashCode.Combine(Tooltip, (int)Flags, DisplayName);

		public static bool operator ==(EditorOnlyPortInfo left, EditorOnlyPortInfo right) => left.Equals(right);

		public static bool operator !=(EditorOnlyPortInfo left, EditorOnlyPortInfo right) => !left.Equals(right);
	}
#endif
	
	/// <summary>
	/// Class that describe port attributes for it's creation
	/// </summary>
	public class PortData : IEquatable<PortData>
	{
		/// <summary>
		/// Unique identifier for the port
		/// </summary>
		public string identifier;

		/// <summary>
		/// The type that will be used for coloring with the type stylesheet
		/// </summary>
		public Type displayType;

		/// <summary>
		/// If the port accept multiple connection
		/// </summary>
		public bool acceptMultipleEdges;

		/// <summary>
		/// Is the port vertical
		/// </summary>
		public bool vertical;

		/// <summary>
		/// Does the port require an edge connection?
		/// </summary>
		public bool required;

#if UNITY_EDITOR
		public EditorOnlyPortInfo EditorOnly;
#endif

		public bool Equals(PortData other)
		{
			return other != null
			       && identifier == other.identifier
			       && displayType == other.displayType
			       && acceptMultipleEdges == other.acceptMultipleEdges
#if UNITY_EDITOR
			       && EditorOnly == other.EditorOnly
#endif
			       && vertical == other.vertical
			       && required == other.required;
		}
	}

	/// <summary>
	/// Runtime class that stores all info about one port that is needed for the processing
	/// </summary>
	public class NodePort
	{
		/// <summary>
		/// The actual name of the property behind the port (must be exact, it is used for Reflection)
		/// </summary>
		public readonly string FieldName;

		/// <summary>
		/// The node on which the port is
		/// </summary>
		public readonly BaseNode Owner;
		
		public readonly NodeFieldInformation FieldInfo;

		/// <summary>
		/// Data of the port
		/// </summary>
		public readonly PortData PortData;

		private readonly List<SerializableEdge> _edges = new();
		private static readonly Dictionary<PushDataDelegateKey, PushDataDelegate> s_pushDataDelegates = new();

		private readonly struct PushDataDelegateKey : IEquatable<PushDataDelegateKey>
		{
			private readonly FieldInfo _from;
			private readonly FieldInfo[] _fromChildren;
			private readonly FieldInfo _to;
			private readonly FieldInfo[] _toChildren;

			public PushDataDelegateKey(
				FieldInfo from,
				[CanBeNull] FieldInfo[] fromChildren,
				FieldInfo to,
				[CanBeNull] FieldInfo[] toChildren
			)
			{
				_to = to;
				_from = from;
				_fromChildren = fromChildren ?? Array.Empty<FieldInfo>();
				_toChildren = toChildren ?? Array.Empty<FieldInfo>();
			}

			public bool Equals(PushDataDelegateKey other) =>
				_from.Equals(other._from)
				&& _to.Equals(other._to)
				&& _fromChildren.SequenceEqual(other._fromChildren)
				&& _toChildren.SequenceEqual(other._toChildren);

			public override bool Equals(object obj) => obj is PushDataDelegateKey other && Equals(other);

			public override int GetHashCode()
			{
				int hashCode = HashCode.Combine(_from, _to);
				foreach (FieldInfo fieldInfo in _fromChildren)
				{
					hashCode = HashCode.Combine(hashCode, fieldInfo);
				}
				
				foreach (FieldInfo fieldInfo in _toChildren)
				{
					hashCode = HashCode.Combine(hashCode, fieldInfo);
				}

				return hashCode;
			}

			public static bool operator ==(PushDataDelegateKey left, PushDataDelegateKey right) => left.Equals(right);

			public static bool operator !=(PushDataDelegateKey left, PushDataDelegateKey right) => !left.Equals(right);
		}
		
		/// <summary>
		/// Delegate that is made to send the data from this port to another port connected through an edge
		/// This is an optimization compared to dynamically setting values using Reflection (which is really slow)
		/// More info: https://codeblog.jonskeet.uk/2008/08/09/making-reflection-fly-and-exploring-delegates/
		/// </summary>
		private delegate void PushDataDelegate(BaseNode from, BaseNode to);
		
		private static bool GetPushDataDelegate(SerializableEdge edge, out PushDataDelegate edgeDelegate)
		{
			PushDataDelegateKey key = new(
				edge.FromPort.FieldInfo,
				edge.FromPort.FieldInfoChildren,
				edge.ToPort.FieldInfo,
				edge.ToPort.FieldInfoChildren
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
		/// <param name="portData">Data of the port</param>
		public NodePort(BaseNode owner, NodeFieldInformation nodeFieldInfo, PortData portData)
		{
			FieldName = nodeFieldInfo.Path.FieldName;
			Owner = owner;
			PortData = portData;

			FieldInfo = nodeFieldInfo.FieldInfo;
		}

		public NodePort(FieldInfo fieldInfo)
		{
			FieldInfo = fieldInfo;
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

		private static readonly ParameterExpression[] s_params = new ParameterExpression[2];

		private static PushDataDelegate CreatePushDataDelegateForEdge(SerializableEdge edge)
		{
			try
			{
				FieldInfo fromFieldInfo = edge.FromPort.FieldInfo;
				FieldInfo toFieldFieldInfo = edge.ToPort.FieldInfo;

				// We keep slow checks inside the editor
#if UNITY_EDITOR
				if (!BaseGraph.TypesAreConnectable(fromFieldInfo.FieldType, toFieldFieldInfo.FieldType))
				{
					Debug.LogError($"[NodeGraph] Can't convert from {fromFieldInfo.FieldType} to {toFieldFieldInfo.FieldType}, " +
					               "you must specify a custom port function (i.e CustomPortInput or CustomPortOutput) for non-implicit conversions. " +
					               $" {edge.FromNode} -> {edge.ToNode}");
					return null;
				}
#endif
				
				ParameterExpression fromParam = Expression.Parameter(typeof(BaseNode), "from");
				ParameterExpression toParam = Expression.Parameter(typeof(BaseNode), "to");
				s_params[0] = fromParam;
				s_params[1] = toParam;
				
				UnaryExpression fromConverted = Expression.Convert(fromParam, fromFieldInfo.DeclaringType!);
				UnaryExpression toConverted = Expression.Convert(toParam, toFieldFieldInfo.DeclaringType!);

				Expression fromParamField = Expression.Field(fromConverted, fromFieldInfo);
				Expression toParamField = Expression.Field(toConverted, toFieldFieldInfo);

				Type toType = edge.ToPort.PortData.displayType ?? toFieldFieldInfo.FieldType;
				Type fromType = edge.FromPort.PortData.displayType ?? fromFieldInfo.FieldType;

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
						fromParamField = Expression.Convert(fromParamField, toFieldFieldInfo.FieldType);
					}
					else // otherwise we cast
					{
						fromParamField = Expression.Convert(fromParamField, toFieldFieldInfo.FieldType);
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
			// Clear lists, set classes to null and struct to default value.
			if (typeof(IList).IsAssignableFrom(FieldInfo.FieldType))
				(FieldInfo.GetValue(Owner) as IList)?.Clear();
			else if (FieldInfo.FieldType.GetTypeInfo().IsClass)
				FieldInfo.SetValue(Owner, null);
			else
			{
				try
				{
					FieldInfo.SetValue(Owner, Activator.CreateInstance(FieldInfo.FieldType));
				}
				catch
				{
					// Catch types that don't have any constructors
				}
			}
		}

		public override string ToString()
			=> $"{FieldName} "
#if UNITY_EDITOR
				+ $"({PortData.EditorOnly.DisplayName}) "
#endif
				+ "edges:\n\t" + string.Join("\n\t", _edges.Select(e => e.ToString()));
	}

	/// <summary>
	/// Container of ports and the edges connected to these ports
	/// </summary>
	public abstract class NodePortContainer : List<NodePort>
	{
		protected readonly BaseNode node;

		public NodePortContainer(BaseNode node)
		{
			this.node = node;
		}

		/// <summary>
		/// Remove an edge that is connected to one of the node in the container
		/// </summary>
		/// <param name="edge"></param>
		public void Remove(SerializableEdge edge)
		{
			ForEach(p => p.Remove(edge));
		}

		/// <summary>
		/// Add an edge that is connected to one of the node in the container
		/// </summary>
		/// <param name="edge"></param>
		public void Add(SerializableEdge edge)
		{
			string portFieldName = edge.ToNode == node ? edge.inputFieldName : edge.outputFieldName;
			string portIdentifier = edge.ToNode == node ? edge.inputPortIdentifier : edge.outputPortIdentifier;

			// Force empty string to null since portIdentifier is a serialized value
			if (string.IsNullOrEmpty(portIdentifier))
				portIdentifier = null;

			NodePort port = this.FirstOrDefault(p => p.FieldName == portFieldName && p.PortData.identifier == portIdentifier);

			if (port == null)
			{
				Debug.LogError("[NodeGraph] The edge can't be properly connected because its ports can't be found.");
				return;
			}

			port.Add(edge);
		}
	}

	/// <inheritdoc/>
	public class NodeInputPortContainer : NodePortContainer
	{
		public NodeInputPortContainer(BaseNode node) : base(node)
		{
		}
	}

	/// <inheritdoc/>
	public class NodeOutputPortContainer : NodePortContainer
	{
		public NodeOutputPortContainer(BaseNode node) : base(node)
		{
		}

		public void PushDatas()
		{
			ForEach(p => p.PushData());
		}
	}
}
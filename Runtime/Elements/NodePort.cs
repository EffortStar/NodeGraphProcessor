// #define DEBUG_LAMBDA

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

		internal readonly NodeFieldInformation _fieldInfo;
		private PortData _portData;

		private readonly List<SerializableEdge> _edges = new();

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
				edge.PushData();
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
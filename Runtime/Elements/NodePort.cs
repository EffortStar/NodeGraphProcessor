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
	/// Class that describe port attributes for it's creation
	/// </summary>
	public class PortData : IEquatable<PortData>
	{
		/// <summary>
		/// Unique identifier for the port
		/// </summary>
		public string identifier;

		/// <summary>
		/// Display name on the node
		/// </summary>
		public string displayName;

		/// <summary>
		/// The type that will be used for coloring with the type stylesheet
		/// </summary>
		public Type displayType;

		/// <summary>
		/// If the port accept multiple connection
		/// </summary>
		public bool acceptMultipleEdges;

		/// <summary>
		/// Port size, will also affect the size of the connected edge
		/// </summary>
		public int sizeInPixel;

		/// <summary>
		/// Tooltip of the port
		/// </summary>
		public string tooltip;

		/// <summary>
		/// Is the port vertical
		/// </summary>
		public bool vertical;

		/// <summary>
		/// Does the port require an edge connection?
		/// </summary>
		public bool required;

		public bool Equals(PortData other)
		{
			return other != null
			       && identifier == other.identifier
			       && displayName == other.displayName
			       && displayType == other.displayType
			       && acceptMultipleEdges == other.acceptMultipleEdges
			       && sizeInPixel == other.sizeInPixel
			       && tooltip == other.tooltip
			       && vertical == other.vertical
			       && required == other.required;
		}

		public void CopyFrom(PortData other)
		{
			identifier = other.identifier;
			displayName = other.displayName;
			displayType = other.displayType;
			acceptMultipleEdges = other.acceptMultipleEdges;
			sizeInPixel = other.sizeInPixel;
			tooltip = other.tooltip;
			vertical = other.vertical;
			required = other.required;
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
		public readonly string fieldName;

		/// <summary>
		/// The node on which the port is
		/// </summary>
		public readonly BaseNode owner;

		/// <summary>
		/// The fieldInfo from the fieldName
		/// </summary>
		public readonly FieldInfo fieldInfo;

		/// <summary>
		/// Data of the port
		/// </summary>
		public readonly PortData portData;

		private readonly List<SerializableEdge> _edges = new();
		private readonly Dictionary<SerializableEdge, PushDataDelegate> _pushDataDelegates = new();
		[CanBeNull] private List<SerializableEdge> _edgeWithRemoteCustomIO;

		private bool GetPushDataDelegate(SerializableEdge edge, out PushDataDelegate edgeDelegate)
		{
			if (_pushDataDelegates.TryGetValue(edge, out edgeDelegate))
			{
				return true;
			}

			edgeDelegate = CreatePushDataDelegateForEdge(edge);

			if (edgeDelegate != null)
			{
				_pushDataDelegates[edge] = edgeDelegate;
				return true;
			}

			return false;
		}

		private readonly CustomPortIODelegate _customPortIOMethod;

		/// <summary>
		/// Delegate that is made to send the data from this port to another port connected through an edge
		/// This is an optimization compared to dynamically setting values using Reflection (which is really slow)
		/// More info: https://codeblog.jonskeet.uk/2008/08/09/making-reflection-fly-and-exploring-delegates/
		/// </summary>
		public delegate void PushDataDelegate();


		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="owner">owner node</param>
		/// <param name="nodeFieldInfo">Complete info about the field</param>
		/// <param name="portData">Data of the port</param>
		public NodePort(BaseNode owner, NodeFieldInformation nodeFieldInfo, PortData portData)
		{
			fieldName = nodeFieldInfo.fieldName;
			this.owner = owner;
			this.portData = portData;

			fieldInfo = nodeFieldInfo.info;
			_customPortIOMethod = CustomPortIO.GetCustomPortMethod(owner.GetType(), fieldName);
		}

		/// <summary>
		/// Connect an edge to this port
		/// </summary>
		/// <param name="edge"></param>
		public void Add(SerializableEdge edge)
		{
			if (!_edges.Contains(edge))
				_edges.Add(edge);

			if (edge.ToNode == owner)
			{
				if (edge.FromPort._customPortIOMethod != null)
				{
					_edgeWithRemoteCustomIO ??= new List<SerializableEdge>();
					_edgeWithRemoteCustomIO.Add(edge);
				}
			}
			else
			{
				if (edge.ToPort._customPortIOMethod != null)
				{
					_edgeWithRemoteCustomIO ??= new List<SerializableEdge>();
					_edgeWithRemoteCustomIO.Add(edge);
				}
			}

			// NOTE: this is slowing down code reload speeds so much that any warnings here aren't worth the trouble.
/*#if UNITY_EDITOR
			//if we have a custom io implementation, we don't need to genereate the defaut one
			if (edge.ToPort._customPortIOMethod != null || edge.FromPort._customPortIOMethod != null)
				return;

			// In the editor we create delegates immediately as they might provide some error feedback.
			// At runtime they're deferred to GetPushDataDelegate.
			PushDataDelegate edgeDelegate = CreatePushDataDelegateForEdge(edge);

			if (edgeDelegate != null)
				_pushDataDelegates[edge] = edgeDelegate;
#endif*/
		}

		PushDataDelegate CreatePushDataDelegateForEdge(SerializableEdge edge)
		{
			static FieldInfo GetFieldInfo(BaseNode node, string name)
			{
				Type type = node.GetType();
				do
				{
					FieldInfo result = type.GetField(name,
						BindingFlags.Public
						| BindingFlags.NonPublic
						| BindingFlags.Instance
						| BindingFlags.DeclaredOnly
					);
					if (result != null)
					{
						return result;
					}
					
					type = type.BaseType;
				} while (type != null && type != typeof(BaseNode));
				
				throw new ArgumentException($"Field of name \"{name}\" could not be found in {node}.");
			}
			
			try
			{
				//Creation of the delegate to move the data from the input node to the output node:
				FieldInfo inputField = GetFieldInfo(edge.ToNode, edge.inputFieldName);
				FieldInfo outputField = GetFieldInfo(edge.FromNode, edge.outputFieldName);

				// ReSharper disable JoinDeclarationAndInitializer
				Type inType, outType;
				// ReSharper restore JoinDeclarationAndInitializer

#if DEBUG_LAMBDA
				return new PushDataDelegate(() => {
					var outValue = outputField.GetValue(edge.outputNode);
					inType = edge.inputPort.portData.displayType ?? inputField.FieldType;
					outType = edge.outputPort.portData.displayType ?? outputField.FieldType;
					Debug.Log($"Push: {inType}({outValue}) -> {outType} | {owner.name}");

					object convertedValue = outValue;
					if (TypeAdapter.AreAssignable(outType, inType))
					{
						var conversionMethod = TypeAdapter.GetConversionMethod(outType, inType);
						Debug.Log("Conversion method: " + conversionMethod.Name);
						convertedValue = conversionMethod.Invoke(null, new object[]{ outValue });
					}

					inputField.SetValue(edge.inputNode, convertedValue);
				});
#endif

// We keep slow checks inside the editor
#if UNITY_EDITOR
				if (!BaseGraph.TypesAreConnectable(outputField.FieldType, inputField.FieldType))
				{
					Debug.LogError($"Can't convert from {outputField.FieldType} to {inputField.FieldType}, " +
					               "you must specify a custom port function (i.e CustomPortInput or CustomPortOutput) for non-implicit conversions. " +
					               $" {edge.FromNode} -> {edge.ToNode}");
					return null;
				}
#endif

				Expression inputParamField = Expression.Field(Expression.Constant(edge.ToNode), inputField);
				Expression outputParamField = Expression.Field(Expression.Constant(edge.FromNode), outputField);

				inType = edge.ToPort.portData.displayType ?? inputField.FieldType;
				outType = edge.FromPort.portData.displayType ?? outputField.FieldType;

				// If there is a user defined conversion function, then we call it
				if (TypeAdapter.AreAssignable(outType, inType))
				{
					// We add a cast in case there we're calling the conversion method with a base class parameter (like object)
					UnaryExpression convertedParam = Expression.Convert(outputParamField, outType);
					outputParamField = Expression.Call(TypeAdapter.GetConversionMethod(outType, inType), convertedParam);
					// In case there is a custom port behavior in the output, then we need to re-cast to the base type because
					// the conversion method return type is not always assignable directly:
					outputParamField = Expression.Convert(outputParamField, inputField.FieldType);
				}
				else // otherwise we cast
					outputParamField = Expression.Convert(outputParamField, inputField.FieldType);

				BinaryExpression assign = Expression.Assign(inputParamField, outputParamField);
				return Expression.Lambda<PushDataDelegate>(assign).Compile();
			}
			catch (Exception e)
			{
				Debug.LogError(e);
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

			_pushDataDelegates.Remove(edge);
			_edgeWithRemoteCustomIO?.Remove(edge);
			_edges.Remove(edge);
		}

		/// <summary>
		/// Get all the edges connected to this port
		/// </summary>
		/// <returns></returns>
		public List<SerializableEdge> GetEdges() => _edges;

		/// <summary>
		/// Push the value of the port through the edges
		/// This method can only be called on output ports
		/// </summary>
		public void PushData()
		{
			if (_customPortIOMethod != null)
			{
				_customPortIOMethod(owner, _edges, this);
				return;
			}

			foreach (SerializableEdge edge in _edges)
			{
				if (GetPushDataDelegate(edge, out PushDataDelegate edgeDelegate))
					edgeDelegate();
			}

			if (_edgeWithRemoteCustomIO == null || _edgeWithRemoteCustomIO.Count == 0)
				return;

			//if there are custom IO implementation on the other ports, they'll need our value in the passThrough buffer
			object ourValue = fieldInfo.GetValue(owner);
			foreach (SerializableEdge edge in _edgeWithRemoteCustomIO)
				edge.PassThroughBuffer = ourValue;
		}

		/// <summary>
		/// Reset the value of the field to default if possible
		/// </summary>
		public void ResetToDefault()
		{
			// Clear lists, set classes to null and struct to default value.
			if (typeof(IList).IsAssignableFrom(fieldInfo.FieldType))
				(fieldInfo.GetValue(owner) as IList)?.Clear();
			else if (fieldInfo.FieldType.GetTypeInfo().IsClass)
				fieldInfo.SetValue(owner, null);
			else
			{
				try
				{
					fieldInfo.SetValue(owner, Activator.CreateInstance(fieldInfo.FieldType));
				}
				catch
				{
					// Catch types that don't have any constructors
				}
			}
		}

		/// <summary>
		/// Pull values from the edge (in case of a custom conversion method)
		/// This method can only be called on input ports
		/// </summary>
		public void PullData()
		{
			if (_customPortIOMethod != null)
			{
				_customPortIOMethod(owner, _edges, this);
				return;
			}

			// check if this port have connection to ports that have custom output functions
			if (_edgeWithRemoteCustomIO == null || _edgeWithRemoteCustomIO.Count == 0)
				return;

			// Only one input connection is handled by this code, if you want to
			// take multiple inputs, you must create a custom input function see CustomPortsNode.cs
			if (_edges.Count > 0)
			{
				object passThroughObject = _edges.First().PassThroughBuffer;

				// We do an extra conversion step in case the buffer output is not compatible with the input port
				if (passThroughObject != null)
					if (TypeAdapter.AreAssignable(fieldInfo.FieldType, passThroughObject.GetType()))
						passThroughObject = TypeAdapter.Convert(passThroughObject, fieldInfo.FieldType);

				fieldInfo.SetValue(owner, passThroughObject);
			}
		}
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

			NodePort port = this.FirstOrDefault(p => p.fieldName == portFieldName && p.portData.identifier == portIdentifier);

			if (port == null)
			{
				Debug.LogError("The edge can't be properly connected because it's ports can't be found");
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

		public void PullDatas()
		{
			ForEach(p => p.PullData());
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
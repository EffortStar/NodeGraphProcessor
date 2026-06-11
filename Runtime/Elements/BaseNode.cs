using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Serialization;

namespace GraphProcessor
{
	[Serializable]
	public abstract class BaseNode
	{
		/// <summary>
		/// Name of the node, it will be displayed in the title section
		/// </summary>
		/// <returns></returns>
		public virtual string name => GetType().Name;

		//id
		public string GUID;

		/// <summary>True if the node can be deleted, false otherwise</summary>
		public virtual bool deletable => true;

		/// <summary>
		/// Container of input ports
		/// </summary>
		[NonSerialized] public readonly NodeInputPortContainer InputPorts;

		/// <summary>
		/// Container of output ports
		/// </summary>
		[NonSerialized] public readonly NodeOutputPortContainer OutputPorts;

		public IEnumerable<NodePort> AllPorts => InputPorts.Concat(OutputPorts);

		//Node view datas
		public Vector2 position;

		/// <summary>
		/// Is the node expanded
		/// </summary>
		// ReSharper disable once NotAccessedField.Global -- serialized
		public bool expanded;

		/// <summary>
		/// Is debug visible
		/// </summary>
		public bool debug;

		public event Action<string, BadgeMessageType> OnMessageAdded;
		public event Action<string> OnMessageRemoved;

		/// <summary>
		/// Does the node needs to be visible in the inspector (when selected).
		/// </summary>
		public virtual bool NeedsInspector => false;

		/// <summary>
		/// Is the node created from a duplicate operation (either ctrl-D or copy/paste).
		/// </summary>
		public bool CreatedFromDuplication { get; internal set; } = false;

		[NonSerialized] private readonly NodeInformation _info;

		[NonSerialized] private List<string> _messages = new();

		[NonSerialized] protected BaseGraph graph;

		private struct PortUpdate
		{
			public List<string> FieldPaths;
			public BaseNode Node;

			public void Deconstruct(out List<string> fieldNames, out BaseNode node)
			{
				fieldNames = FieldPaths;
				node = Node;
			}
		}

		/// <summary>
		/// Creates a node of type T at a certain position
		/// </summary>
		/// <param name="position">position in the graph in pixels</param>
		/// <typeparam name="T">type of the node</typeparam>
		/// <returns>the node instance</returns>
		public static T CreateFromType<T>(Vector2 position) where T : BaseNode
			=> CreateFromType(typeof(T), position) as T;

		/// <summary>
		/// Creates a node of type nodeType at a certain position
		/// </summary>
		/// <param name="position">position in the graph in pixels</param>
		/// <typeparam name="nodeType">type of the node</typeparam>
		/// <returns>the node instance</returns>
		public static BaseNode CreateFromType(Type nodeType, Vector2 position)
		{
			if (!nodeType.IsSubclassOf(typeof(BaseNode)))
			{
				return null;
			}

			BaseNode node;
			try
			{
				node = (BaseNode)Activator.CreateInstance(nodeType);
			}
			catch (Exception)
			{
				var genericNodeAttribute = (GenericNodeAttribute)Attribute.GetCustomAttribute(nodeType, typeof(GenericNodeAttribute));
				if (genericNodeAttribute == null)
				{
					throw;
				}

				nodeType = nodeType.MakeGenericType(genericNodeAttribute.BaseConstraintType);
				node = (BaseNode)Activator.CreateInstance(nodeType);
			}

			node.position = position;
			try
			{
				node.OnNodeCreated();
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}

			return node;
		}

#region Initialization

		public BaseGraph Graph
		{
			get => graph;
			set => graph = value;
		}

		// called by the BaseGraph when the node is added to the graph
		public void Initialize(BaseGraph graph)
		{
			try
			{
				this.graph = graph;
				Enable();
				InitializePorts();
			}
			catch (Exception e)
			{
				Debug.LogError($"[NodeGraph] Error while processing \"{this}\".");
				Debug.LogException(e);
			}
		}

		/// <summary>
		/// Use this function to initialize anything related to ports generation in your node
		/// This will allow the node creation menu to correctly recognize ports that can be connected between nodes
		/// </summary>
		public virtual void InitializePorts()
		{
			InputPorts.Clear();
			OutputPorts.Clear();

			foreach (NodeFieldInformation nodeField in OverrideFieldOrder(_info.Ports.Values))
			{
				// If we don't have nested children, we just have to create a simple port.
				AddPort(nodeField);
			}

			foreach (MethodInfo customPortMethod in _info.CustomPorts)
			{
				var ports = (IEnumerable<PortData>)customPortMethod.Invoke(this, null);
				foreach (PortData portData in ports)
				{
					AddPort(portData);
				}
			}
		}

		/// <summary>
		/// Override the field order inside the node. It allows to re-order all the ports and field in the UI.
		/// </summary>
		/// <param name="fields">List of fields to sort</param>
		/// <returns>Sorted list of fields</returns>
		public static IEnumerable<FieldInfo> OverrideFieldOrder(IEnumerable<FieldInfo> fields)
		{
			// Order by MetadataToken and inheritance level to sync the order with the port order (make sure FieldDrawers are next to the correct port)
			return fields.OrderByDescending(f => (GetFieldInheritanceLevel(f) << 32) | (uint)f.MetadataToken);

			long GetFieldInheritanceLevel(FieldInfo f)
			{
				var level = 0;
				Type t = f.DeclaringType;
				while (t != null)
				{
					t = t.BaseType;
					level++;
				}

				return level;
			}
		}

		/// <summary>
		/// Override the field order inside the node. It allows to re-order all the ports and field in the UI.
		/// </summary>
		/// <param name="fields">List of fields to sort</param>
		/// <returns>Sorted list of fields</returns>
		public static IEnumerable<NodeFieldInformation> OverrideFieldOrder(IEnumerable<NodeFieldInformation> fields)
		{
			// Order by MetadataToken and inheritance level to sync the order with the port order (make sure FieldDrawers are next to the correct port)
			return fields
				.OrderBy(f => GetFieldInheritanceLevel(f.Path.Root.FieldInfo))
				.ThenBy(f => f.Path.Root.FieldInfo.MetadataToken)
				.ThenBy(f =>
					{
						long level = 0;
						int i = f.Path.Depth + 1;
						for (NodeFieldPath path = f.Path; path != null; path = path.Parent, i--)
						{
							level += (uint)path.FieldInfo.MetadataToken * (i * 10);
						}

						return level;
					}
				);

			long GetFieldInheritanceLevel(FieldInfo f)
			{
				var level = 0;
				Type t = f.DeclaringType;
				while (t != null)
				{
					t = t.BaseType;
					level++;
				}

				return level;
			}
		}

		protected BaseNode()
		{
			InputPorts = new NodeInputPortContainer(this);
			OutputPorts = new NodeOutputPortContainer(this);
			_info = NodeInformation.GetInfoGroup(GetType());
		}

		internal void DestroyInternal()
		{
			try
			{
				Destroy();
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
		}

		/// <summary>
		/// Called only when the node is created, not when instantiated
		/// </summary>
		public virtual void OnNodeCreated() => GUID = Guid.NewGuid().ToString();

#endregion

#region Events and Processing

		public void OnEdgeConnected(SerializableEdge edge)
		{
			bool input = edge.ToNode == this;
			NodePortContainer portCollection = input ? InputPorts : OutputPorts;
			portCollection.Add(edge);
		}

		protected virtual bool CanResetPort(NodePort port) => true;

		public void OnEdgeDisconnected(SerializableEdge edge)
		{
			if (edge == null)
				return;

			if (edge.ToNode == this)
			{
				InputPorts.Remove(edge);
			}
			else if (edge.FromNode == this)
			{
				OutputPorts.Remove(edge);
			}

			// Reset default values of input port:
			if (edge.ToNode != null)
			{
				bool haveConnectedEdges = edge.ToNode.InputPorts.Where(p => p.FieldPath == edge.InputFieldPath).Any(p => p.Edges.Count != 0);
				if (edge.ToNode == this && !haveConnectedEdges && CanResetPort(edge.ToPort))
					edge.ToPort?.ResetToDefault();
			}
		}

		public void OnProcess()
		{
			try
			{
				Process();
			}
			catch (Exception e)
			{
				Debug.LogError($"[NodeGraph] Error while processing \"{this}\".");
				Debug.LogException(e);
			}

			OutputPorts.PushDatas();
		}

		/// <summary>
		/// Called when the node is enabled
		/// </summary>
		protected virtual void Enable() { }

		/// <summary>
		/// Called when the node is removed
		/// </summary>
		protected virtual void Destroy() { }

		/// <summary>
		/// Override this method to implement custom processing
		/// </summary>
		protected virtual void Process() { }

#endregion

#region API and utils

		/// <summary>
		/// Add a port
		/// </summary>
		/// <param name="input">is input port</param>
		/// <param name="fieldName">C# field name</param>
		/// <param name="portData">Data of the port</param>
		public void AddPort(NodeFieldInformation fieldInfo)
		{
			if (fieldInfo.IsInput)
				InputPorts.Add(new NodePort(this, fieldInfo));
			else
				OutputPorts.Add(new NodePort(this, fieldInfo));
		}

		private void AddPort(PortData portData)
		{
			if (portData.IsInput)
				InputPorts.Add(new NodePort(this, portData));
			else
				OutputPorts.Add(new NodePort(this, portData));
		}

		/// <summary>
		/// Remove a port
		/// </summary>
		/// <param name="input">is input port</param>
		/// <param name="port">the port to delete</param>
		public void RemovePort(bool input, NodePort port)
		{
			if (input)
				InputPorts.Remove(port);
			else
				OutputPorts.Remove(port);
		}

		/// <summary>
		/// Remove port(s) from field name
		/// </summary>
		/// <param name="input">is input</param>
		/// <param name="fieldName">C# field name</param>
		public void RemovePort(bool input, string fieldName)
		{
			if (input)
				InputPorts.RemoveAll(p => p.FieldPath == fieldName);
			else
				OutputPorts.RemoveAll(p => p.FieldPath == fieldName);
		}

		/// <summary>
		/// Get all the nodes connected to the input ports of this node
		/// </summary>
		/// <returns>an enumerable of node</returns>
		public List<BaseNode> GetInputNodes(List<BaseNode> results)
		{
			foreach (NodePort port in InputPorts)
			{
				foreach (SerializableEdge edge in port.Edges)
					results.Add(edge.FromNode);
			}

			return results;
		}

		/// <summary>
		/// Get all the nodes connected to the output ports of this node
		/// </summary>
		/// <returns>an enumerable of node</returns>
		public List<BaseNode> GetOutputNodes(List<BaseNode> results)
		{
			foreach (NodePort port in OutputPorts)
			{
				foreach (SerializableEdge edge in port.Edges)
					results.Add(edge.ToNode);
			}

			return results;
		}

		/// <summary>
		/// Return a node matching the condition in the dependencies of the node
		/// </summary>
		/// <param name="condition">Condition to choose the node</param>
		/// <returns>Matched node or null</returns>
		public BaseNode FindInDependencies(Func<BaseNode, bool> condition)
		{
			var dependencies = new Stack<BaseNode>();

			dependencies.Push(this);

			var depth = 0;
			while (dependencies.Count > 0)
			{
				BaseNode node = dependencies.Pop();

				// Guard for infinite loop (faster than a HashSet based solution)
				depth++;
				if (depth > 2000)
					break;

				if (condition(node))
					return node;

				using var _ = ListPool<BaseNode>.Get(out var nodes);
				foreach (BaseNode dep in node.GetInputNodes(nodes))
					dependencies.Push(dep);
			}

			return null;
		}

		/// <summary>
		/// Get the port from field name and identifier
		/// </summary>
		/// <param name="fieldPath">C# field name</param>
		/// <param name="identifier">Unique port identifier</param>
		/// <returns></returns>
		public NodePort GetPort(string fieldPath, string identifier)
		{
			// ReSharper disable once LoopCanBeConvertedToQuery
			foreach (NodePort port in AllPorts)
			{
				bool bothNull = string.IsNullOrEmpty(identifier) && string.IsNullOrEmpty(port.Identifier);
				if (port.FieldPath == fieldPath && (bothNull || identifier == port.Identifier))
				{
					return port;
				}
			}

			return null;
		}

		/// <summary>
		/// Get the port from field name and identifier ONLY using FormerlySerializedAsAttribute.<br/>
		/// To be called sparingly when <see cref="GetPort"/> fails, in cases where deserializing and unexpectedly ports are missing.
		/// </summary>
		public virtual bool TryGetFallbackPort(ref string fieldPath, ref string identifier, out NodePort value)
		{
			bool identifierIsNull = string.IsNullOrEmpty(identifier);

			if (!identifierIsNull)
			{
				var fallbackPath = $"{fieldPath}{NodeFieldPath.Separator}{identifier}";
				foreach (NodePort port in AllPorts)
				{
					if (string.IsNullOrEmpty(port.Identifier) && port.FieldPath == fallbackPath)
					{
						value = port;
						fieldPath = port.FieldPath;
						identifier = port.Identifier;
						return true;
					}
				}
			}

			foreach (NodePort port in AllPorts)
			{
				bool bothNull = identifierIsNull && string.IsNullOrEmpty(port.Identifier);
				if (!bothNull && identifier != port.Identifier)
				{
					continue;
				}

				if (port.FieldInfo == null)
				{
					continue;
				}

				foreach (FormerlySerializedAsAttribute attribute in port.FieldInfo.GetCustomAttributes<FormerlySerializedAsAttribute>())
				{
					if (attribute.oldName != fieldPath) continue;
					value = port;
					fieldPath = port.FieldPath;
					identifier = port.Identifier;
					return true;
				}
			}

			value = null;
			return false;
		}

		/// <summary>
		/// Return all the connected edges of the node
		/// </summary>
		/// <returns></returns>
		public IEnumerable<SerializableEdge> GetAllEdges() => AllPorts.SelectMany(port => port.Edges);

		/// <summary>
		/// Add a message on the node
		/// </summary>
		/// <param name="message"></param>
		/// <param name="messageType"></param>
		public void AddMessage(string message, BadgeMessageType messageType)
		{
			if (_messages.Contains(message))
				return;

			OnMessageAdded?.Invoke(message, messageType);
			_messages.Add(message);
		}

		/// <summary>
		/// Remove a message on the node
		/// </summary>
		/// <param name="message"></param>
		public void RemoveMessage(string message)
		{
			OnMessageRemoved?.Invoke(message);
			_messages.Remove(message);
		}

		/// <summary>
		/// Remove a message that contains
		/// </summary>
		/// <param name="subMessage"></param>
		public void RemoveMessageContains(string subMessage)
		{
			string toRemove = _messages.Find(m => m.Contains(subMessage));
			_messages.Remove(toRemove);
			OnMessageRemoved?.Invoke(toRemove);
		}

		/// <summary>
		/// Remove all messages on the node
		/// </summary>
		public void ClearMessages()
		{
			foreach (string message in _messages)
				OnMessageRemoved?.Invoke(message);
			_messages.Clear();
		}

#endregion

		public override string ToString() => $"{name} ({GetType().Name}) (in: {graph.name})";
	}
}
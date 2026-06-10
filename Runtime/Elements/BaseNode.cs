using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

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
		[NonSerialized] public readonly NodeInputPortContainer inputPorts;

		/// <summary>
		/// Container of output ports
		/// </summary>
		[NonSerialized] public readonly NodeOutputPortContainer outputPorts;

		public IEnumerable<NodePort> AllPorts => inputPorts.Concat(outputPorts);

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

		public event Action<string, BadgeMessageType> onMessageAdded;
		public event Action<string> onMessageRemoved;


		/// <summary>
		/// Triggered after a single/list of port(s) is updated, the parameter is the field name
		/// </summary>
		public event Action<string> onPortsUpdated;

		/// <summary>
		/// Does the node needs to be visible in the inspector (when selected).
		/// </summary>
		public virtual bool needsInspector => false;

		/// <summary>
		/// Is the node created from a duplicate operation (either ctrl-D or copy/paste).
		/// </summary>
		public bool createdFromDuplication { get; internal set; } = false;

		[NonSerialized] internal readonly NodeInformation info;
		
		[NonSerialized] private List<string> messages = new();

		[NonSerialized] protected BaseGraph graph;

		private struct PortUpdate
		{
			public List<string> fieldPaths;
			public BaseNode node;

			public void Deconstruct(out List<string> fieldNames, out BaseNode node)
			{
				fieldNames = this.fieldPaths;
				node = this.node;
			}
		}

		// Used in port update algorithm
		private Stack<PortUpdate> fieldsToUpdate = new();
		private HashSet<PortUpdate> updatedFields = new();

		/// <summary>
		/// Creates a node of type T at a certain position
		/// </summary>
		/// <param name="position">position in the graph in pixels</param>
		/// <typeparam name="T">type of the node</typeparam>
		/// <returns>the node instance</returns>
		public static T CreateFromType<T>(Vector2 position) where T : BaseNode
		{
			return CreateFromType(typeof(T), position) as T;
		}

		/// <summary>
		/// Creates a node of type nodeType at a certain position
		/// </summary>
		/// <param name="position">position in the graph in pixels</param>
		/// <typeparam name="nodeType">type of the node</typeparam>
		/// <returns>the node instance</returns>
		public static BaseNode CreateFromType(Type nodeType, Vector2 position)
		{
			if (!nodeType.IsSubclassOf(typeof(BaseNode)))
				return null;

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
			inputPorts.Clear();
			outputPorts.Clear();

			foreach (NodeFieldInformation nodeField in OverrideFieldOrder(info.Ports.Values))
			{
				// If we don't have nested children, we just have to create a simple port.
				AddPort(
					nodeField,
					new PortData
					{
						acceptMultipleEdges = nodeField.AllowMultiple,
#if UNITY_EDITOR
						EditorOnly = nodeField.EditorOnly,
#endif
						vertical = nodeField.IsVertical,
						required = nodeField.IsRequired
					}
				);
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
			return fields.OrderByDescending(f => (GetFieldInheritanceLevel(f.Path.FieldInfo) << 32) | (uint)f.Path.FieldInfo.MetadataToken);

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
			inputPorts = new NodeInputPortContainer(this);
			outputPorts = new NodeOutputPortContainer(this);
			info = NodeInformation.GetInfoGroup(GetType());
		}

		/// <summary>
		/// Update all ports of the node
		/// </summary>
		public bool UpdateAllPorts()
		{
			var changed = false;

			foreach (NodeFieldInformation field in OverrideFieldOrder(info.Ports.Values))
			{
				changed |= UpdatePortsForField(field.Path.FieldPath);
			}

			return changed;
		}

		/// <summary>
		/// Update the ports related to one C# property field (only for this node)
		/// </summary>
		/// <param name="fieldName"></param>
		public bool UpdatePortsForFieldLocal(string fieldName, bool sendPortUpdatedEvent = true)
		{
			var changed = false;

			if (!info.Ports.TryGetValue(fieldName, out NodeFieldInformation fieldInfo))
				return false;

			var finalPorts = new List<string>();

			NodePortContainer portCollection = fieldInfo.IsInput ? inputPorts : outputPorts;

			// Gather all fields for this port (before to modify them)
			NodePort[] nodePorts = portCollection.Where(p => p.FieldPath == fieldName).ToArray();

			// TODO
			// Remove only the ports that are no more in the list
			{
				foreach (NodePort currentPort in nodePorts)
				{
					// If the current port does not appear in the list of final ports, we remove it
					// if (finalPorts.All(id => id != currentPort.portData.identifier))
					var all = true;
					// ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
					foreach (string id in finalPorts)
					{
						if (id != currentPort.PortData.identifier) continue;
						all = false;
						break;
					}

					if (!all) continue;
					RemovePort(fieldInfo.IsInput, currentPort);
					changed = true;
				}
			}

			// Make sure the port order is correct:
			portCollection.Sort((p1, p2) =>
			{
				int p1Index = finalPorts.FindIndex(id => p1.PortData.identifier == id);
				int p2Index = finalPorts.FindIndex(id => p2.PortData.identifier == id);

				if (p1Index == -1 || p2Index == -1)
					return 0;

				return p1Index.CompareTo(p2Index);
			});

			if (sendPortUpdatedEvent)
				onPortsUpdated?.Invoke(fieldName);

			return changed;
		}

		/// <summary>
		/// Update the ports related to one C# property field and all connected nodes in the graph
		/// </summary>
		/// <param name="fieldPath"></param>
		public bool UpdatePortsForField(string fieldPath, bool sendPortUpdatedEvent = true)
		{
			var changed = false;

			fieldsToUpdate.Clear();
			updatedFields.Clear();

			fieldsToUpdate.Push(new PortUpdate { fieldPaths = new List<string> { fieldPath }, node = this });

			// Iterate through all the ports that needs to be updated, following graph connection when the 
			// port is updated. This is required ton have type propagation multiple nodes that changes port types
			// are connected to each other (i.e. the relay node)
			while (fieldsToUpdate.Count != 0)
			{
				(List<string> fields, BaseNode node) = fieldsToUpdate.Pop();

				// Avoid updating twice a port
				if (updatedFields.Any(t => t.node == node && fields.SequenceEqual(t.fieldPaths)))
					continue;
				updatedFields.Add(new PortUpdate { fieldPaths = fields, node = node });

				foreach (string field in fields)
				{
					if (!node.UpdatePortsForFieldLocal(field, sendPortUpdatedEvent)) continue;
					changed = true;
				}
			}

			return changed;
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
			NodePortContainer portCollection = input ? inputPorts : outputPorts;

			portCollection.Add(edge);

			UpdateAllPorts();
		}

		protected virtual bool CanResetPort(NodePort port) => true;

		public void OnEdgeDisconnected(SerializableEdge edge, bool updatePorts = true)
		{
			if (edge == null)
				return;

			if (edge.ToNode == this)
			{
				inputPorts.Remove(edge);
			}
			else if (edge.FromNode == this)
			{
				outputPorts.Remove(edge);
			}

			// Reset default values of input port:
			if (edge.ToNode != null)
			{
				bool haveConnectedEdges = edge.ToNode.inputPorts.Where(p => p.FieldPath == edge.inputFieldName).Any(p => p.Edges.Count != 0);
				if (edge.ToNode == this && !haveConnectedEdges && CanResetPort(edge.ToPort))
					edge.ToPort?.ResetToDefault();
			}

			if (updatePorts)
				UpdateAllPorts();
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

			outputPorts.PushDatas();
		}

		/// <summary>
		/// Called when the node is enabled
		/// </summary>
		protected virtual void Enable()
		{
		}

		/// <summary>
		/// Called when the node is removed
		/// </summary>
		protected virtual void Destroy()
		{
		}

		/// <summary>
		/// Override this method to implement custom processing
		/// </summary>
		protected virtual void Process()
		{
		}

		#endregion

		#region API and utils

		/// <summary>
		/// Add a port
		/// </summary>
		/// <param name="input">is input port</param>
		/// <param name="fieldName">C# field name</param>
		/// <param name="portData">Data of the port</param>
		public void AddPort(NodeFieldInformation fieldInfo, PortData portData)
		{
			// Fixup port data info if needed:
			portData.displayType ??= fieldInfo.FieldType;

			if (fieldInfo.IsInput)
				inputPorts.Add(new NodePort(this, fieldInfo, portData));
			else
				outputPorts.Add(new NodePort(this, fieldInfo, portData));
		}

		/// <summary>
		/// Remove a port
		/// </summary>
		/// <param name="input">is input port</param>
		/// <param name="port">the port to delete</param>
		public void RemovePort(bool input, NodePort port)
		{
			if (input)
				inputPorts.Remove(port);
			else
				outputPorts.Remove(port);
		}

		/// <summary>
		/// Remove port(s) from field name
		/// </summary>
		/// <param name="input">is input</param>
		/// <param name="fieldName">C# field name</param>
		public void RemovePort(bool input, string fieldName)
		{
			if (input)
				inputPorts.RemoveAll(p => p.FieldPath == fieldName);
			else
				outputPorts.RemoveAll(p => p.FieldPath == fieldName);
		}

		/// <summary>
		/// Get all the nodes connected to the input ports of this node
		/// </summary>
		/// <returns>an enumerable of node</returns>
		public List<BaseNode> GetInputNodes(List<BaseNode> results)
		{
			foreach (NodePort port in inputPorts){
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
			foreach (NodePort port in outputPorts){
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
		/// <param name="fieldName">C# field name</param>
		/// <param name="identifier">Unique port identifier</param>
		/// <returns></returns>
		public NodePort GetPort(string fieldName, string identifier)
		{
			return AllPorts.FirstOrDefault(p =>
			{
				bool bothNull = string.IsNullOrEmpty(identifier) && string.IsNullOrEmpty(p.PortData.identifier);
				return p.FieldPath == fieldName && (bothNull || identifier == p.PortData.identifier);
			});
		}

		/// <summary>
		/// Get the port from field name and identifier ONLY using FormerlySerializedAsAttribute.<br/>
		/// To be called sparingly when <see cref="GetPort"/> fails, in cases where deserializing and unexpectedly ports are missing.
		/// </summary>
		public virtual bool TryGetFallbackPort(ref string fieldName, ref string identifier, out NodePort value)
		{
			bool identifierIsNull = string.IsNullOrEmpty(identifier);
			foreach (NodePort port in AllPorts)
			{
				bool bothNull = identifierIsNull && string.IsNullOrEmpty(port.PortData.identifier);
				if (!bothNull && identifier != port.PortData.identifier) continue;
				foreach (FormerlySerializedAsAttribute attribute in port.FieldInfo.Path.FieldInfo.GetCustomAttributes<FormerlySerializedAsAttribute>())
				{
					if (attribute.oldName != fieldName) continue;
					value = port;
					fieldName = port.FieldPath;
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
			if (messages.Contains(message))
				return;

			onMessageAdded?.Invoke(message, messageType);
			messages.Add(message);
		}

		/// <summary>
		/// Remove a message on the node
		/// </summary>
		/// <param name="message"></param>
		public void RemoveMessage(string message)
		{
			onMessageRemoved?.Invoke(message);
			messages.Remove(message);
		}

		/// <summary>
		/// Remove a message that contains
		/// </summary>
		/// <param name="subMessage"></param>
		public void RemoveMessageContains(string subMessage)
		{
			string toRemove = messages.Find(m => m.Contains(subMessage));
			messages.Remove(toRemove);
			onMessageRemoved?.Invoke(toRemove);
		}

		/// <summary>
		/// Remove all messages on the node
		/// </summary>
		public void ClearMessages()
		{
			foreach (string message in messages)
				onMessageRemoved?.Invoke(message);
			messages.Clear();
		}

		#endregion

		public override string ToString() => $"{name} ({GetType().Name}) (in: {graph.name})";
	}
}
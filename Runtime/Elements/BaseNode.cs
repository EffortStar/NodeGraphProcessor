﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Serialization;

namespace GraphProcessor
{
	public delegate IEnumerable<PortData> CustomPortBehaviorDelegate(List<SerializableEdge> edges);

	public delegate IEnumerable<PortData> CustomPortTypeBehaviorDelegate(string fieldName, string displayName, object value);

	[Serializable]
	public abstract class BaseNode
	{
		[SerializeField] internal string nodeCustomName; // The name of the node in case it was renamed by a user

		/// <summary>
		/// Name of the node, it will be displayed in the title section
		/// </summary>
		/// <returns></returns>
		public virtual string name => GetType().Name;

		/// <summary>
		/// The accent color of the node
		/// </summary>
		public virtual Color color => Color.clear;

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
		/// Can the node be renamed in the UI. By default a node can be renamed by double clicking it's name.
		/// </summary>
		public virtual bool isRenamable => false;

		/// <summary>
		/// Is the node created from a duplicate operation (either ctrl-D or copy/paste).
		/// </summary>
		public bool createdFromDuplication { get; internal set; } = false;

		[NonSerialized] internal Dictionary<string, NodeFieldInformation> nodeFields = new();

		[NonSerialized] internal Dictionary<Type, CustomPortTypeBehaviorDelegate> customPortTypeBehaviorMap = new();

		[NonSerialized] private List<string> messages = new();

		[NonSerialized] protected BaseGraph graph;

		internal class NodeFieldInformation
		{
			public readonly string name;
			public readonly string fieldName;
			public readonly FieldInfo info;
			public readonly bool input;
			public readonly bool isMultiple;
			public readonly string tooltip;
			public CustomPortBehaviorDelegate behavior;
			public readonly bool isRequired;
			public readonly bool vertical;

			public NodeFieldInformation(
				FieldInfo info,
				string name,
				bool input,
				bool isMultiple,
				string tooltip,
				bool vertical,
				CustomPortBehaviorDelegate behavior,
				bool isRequired
			)
			{
				this.input = input;
				this.isMultiple = isMultiple;
				this.info = info;
				this.name = name;
				fieldName = info.Name;
				this.behavior = behavior;
				this.isRequired = isRequired;
				this.tooltip = tooltip;
				this.vertical = vertical;
			}
		}

		private struct PortUpdate
		{
			public List<string> fieldNames;
			public BaseNode node;

			public void Deconstruct(out List<string> fieldNames, out BaseNode node)
			{
				fieldNames = this.fieldNames;
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

			var node = (BaseNode)Activator.CreateInstance(nodeType);

			node.position = position;

			ExceptionToLog.Call(() => node.OnNodeCreated());

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
			this.graph = graph;
			ExceptionToLog.Call(Enable);

			InitializePorts();
		}

		/// <summary>
		/// Use this function to initialize anything related to ports generation in your node
		/// This will allow the node creation menu to correctly recognize ports that can be connected between nodes
		/// </summary>
		public virtual void InitializePorts()
		{
			inputPorts.Clear();
			outputPorts.Clear();

			foreach (FieldInfo key in OverrideFieldOrder(nodeFields.Values.Select(k => k.info)))
			{
				NodeFieldInformation nodeField = nodeFields[key.Name];

				if (HasCustomBehavior(nodeField))
				{
					UpdatePortsForField(nodeField.fieldName, sendPortUpdatedEvent: false);
				}
				else
				{
					// If we don't have a custom behavior on the node, we just have to create a simple port
					AddPort(
						nodeField.input,
						nodeField.fieldName,
						new PortData
						{
							acceptMultipleEdges = nodeField.isMultiple,
							displayName = nodeField.name,
							tooltip = nodeField.tooltip,
							vertical = nodeField.vertical,
							required = nodeField.isRequired
						}
					);
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

		protected BaseNode()
		{
			inputPorts = new NodeInputPortContainer(this);
			outputPorts = new NodeOutputPortContainer(this);

			InitializeInOutDatas();
		}

		/// <summary>
		/// Update all ports of the node
		/// </summary>
		public bool UpdateAllPorts()
		{
			var changed = false;

			foreach (FieldInfo key in OverrideFieldOrder(nodeFields.Values.Select(k => k.info)))
			{
				NodeFieldInformation field = nodeFields[key.Name];
				changed |= UpdatePortsForField(field.fieldName);
			}

			return changed;
		}

		/// <summary>
		/// Update all ports of the node without updating the connected ports. Only use this method when you need to update all the nodes ports in your graph.
		/// </summary>
		public bool UpdateAllPortsLocal()
		{
			var changed = false;

			foreach (FieldInfo key in OverrideFieldOrder(nodeFields.Values.Select(k => k.info)))
			{
				NodeFieldInformation field = nodeFields[key.Name];
				changed |= UpdatePortsForFieldLocal(field.fieldName);
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

			if (!nodeFields.TryGetValue(fieldName, out NodeFieldInformation fieldInfo))
				return false;

			if (!HasCustomBehavior(fieldInfo))
				return false;

			var finalPorts = new List<string>();

			NodePortContainer portCollection = fieldInfo.input ? inputPorts : outputPorts;

			// Gather all fields for this port (before to modify them)
			IEnumerable<NodePort> nodePorts = portCollection.Where(p => p.fieldName == fieldName);
			// Gather all edges connected to these fields:
			List<SerializableEdge> edges = nodePorts.SelectMany(n => n.GetEdges()).ToList();

			if (fieldInfo.behavior != null)
			{
				foreach (PortData portData in fieldInfo.behavior(edges))
					AddPortData(portData);
			}
			else
			{
				CustomPortTypeBehaviorDelegate customPortTypeBehavior = customPortTypeBehaviorMap[fieldInfo.info.FieldType];

				foreach (PortData portData in customPortTypeBehavior(fieldName, fieldInfo.name, fieldInfo.info.GetValue(this)))
					AddPortData(portData);
			}

			void AddPortData(PortData portData)
			{
				NodePort port = nodePorts.FirstOrDefault(n => n.portData.identifier == portData.identifier);
				// Guard using the port identifier so we don't duplicate identifiers
				if (port == null)
				{
					AddPort(fieldInfo.input, fieldName, portData);
					changed = true;
				}
				else
				{
					// in case the port type have changed for an incompatible type, we disconnect all the edges attached to this port
					if (!BaseGraph.TypesAreConnectable(port.portData.displayType, portData.displayType))
					{
						if (this is not SimplifiedRelayNode)
						{
							foreach (SerializableEdge edge in port.GetEdges().ToList())
								graph.Disconnect(edge.GUID);
						}
					}

					// patch the port data
					if (port.portData != portData)
					{
						port.portData.CopyFrom(portData);
						changed = true;
					}
				}

				finalPorts.Add(portData.identifier);
			}

			// TODO
			// Remove only the ports that are no more in the list
			if (nodePorts != null)
			{
				List<NodePort> currentPortsCopy = nodePorts.ToList();
				foreach (NodePort currentPort in currentPortsCopy)
				{
					// If the current port does not appear in the list of final ports, we remove it
					if (finalPorts.All(id => id != currentPort.portData.identifier))
					{
						RemovePort(fieldInfo.input, currentPort);
						changed = true;
					}
				}
			}

			// Make sure the port order is correct:
			portCollection.Sort((p1, p2) =>
			{
				int p1Index = finalPorts.FindIndex(id => p1.portData.identifier == id);
				int p2Index = finalPorts.FindIndex(id => p2.portData.identifier == id);

				if (p1Index == -1 || p2Index == -1)
					return 0;

				return p1Index.CompareTo(p2Index);
			});

			if (sendPortUpdatedEvent)
				onPortsUpdated?.Invoke(fieldName);

			return changed;
		}

		private bool HasCustomBehavior(NodeFieldInformation info)
		{
			if (info.behavior != null)
				return true;

			if (customPortTypeBehaviorMap.ContainsKey(info.info.FieldType))
				return true;

			return false;
		}

		/// <summary>
		/// Update the ports related to one C# property field and all connected nodes in the graph
		/// </summary>
		/// <param name="fieldName"></param>
		public bool UpdatePortsForField(string fieldName, bool sendPortUpdatedEvent = true)
		{
			var changed = false;

			fieldsToUpdate.Clear();
			updatedFields.Clear();

			fieldsToUpdate.Push(new PortUpdate { fieldNames = new List<string> { fieldName }, node = this });

			// Iterate through all the ports that needs to be updated, following graph connection when the 
			// port is updated. This is required ton have type propagation multiple nodes that changes port types
			// are connected to each other (i.e. the relay node)
			while (fieldsToUpdate.Count != 0)
			{
				(List<string> fields, BaseNode node) = fieldsToUpdate.Pop();

				// Avoid updating twice a port
				if (updatedFields.Any(t => t.node == node && fields.SequenceEqual(t.fieldNames)))
					continue;
				updatedFields.Add(new PortUpdate { fieldNames = fields, node = node });

				foreach (string field in fields)
				{
					if (!node.UpdatePortsForFieldLocal(field, sendPortUpdatedEvent)) continue;
					foreach (NodePort port in (NodePortContainer)(node.IsFieldInput(field) ? node.inputPorts : node.outputPorts))
					{
						if (port.fieldName != field)
							continue;

						foreach (SerializableEdge edge in port.GetEdges())
						{
							BaseNode edgeNode = node.IsFieldInput(field) ? edge.FromNode : edge.ToNode;
							List<string> fieldsWithBehavior = edgeNode.nodeFields.Values.Where(HasCustomBehavior).Select(f => f.fieldName).ToList();
							fieldsToUpdate.Push(new PortUpdate { fieldNames = fieldsWithBehavior, node = edgeNode });
						}
					}

					changed = true;
				}
			}

			return changed;
		}

		internal void DestroyInternal() => ExceptionToLog.Call(Destroy);

		/// <summary>
		/// Called only when the node is created, not when instantiated
		/// </summary>
		public virtual void OnNodeCreated() => GUID = Guid.NewGuid().ToString();

		public virtual FieldInfo[] GetNodeFields()
			=> GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

		private void InitializeInOutDatas()
		{
			FieldInfo[] fields = GetNodeFields();

			foreach (FieldInfo field in fields)
			{
				var inputAttribute = field.GetCustomAttribute<InputAttribute>();
				var outputAttribute = field.GetCustomAttribute<OutputAttribute>();

				if (inputAttribute == null && outputAttribute == null)
					continue;

				var isVertical = Attribute.IsDefined(field, typeof(VerticalAttribute));
				var isRequired = Attribute.IsDefined(field, typeof(RequiredPortAttribute));
				var tooltipAttribute = field.GetCustomAttribute<TooltipAttribute>();


				// check if field is a collection type
				bool isMultiple = inputAttribute?.allowMultiple ?? outputAttribute.allowMultiple;
				bool input = inputAttribute != null;
				var tooltip = $"<b>{TypeUtility.FormatTypeName(field.FieldType)}</b>";
				if (tooltipAttribute != null)
				{
					tooltip += $"\n{tooltipAttribute.tooltip}";
				}

				string name = field.Name;
				if (inputAttribute is { name: not null })
					name = inputAttribute.name;
				if (outputAttribute is { name: not null })
					name = outputAttribute.name;

				// By default, we set the behavior to null, if the field have a custom behavior, it will be set in the loop just below
				nodeFields[field.Name] = new NodeFieldInformation(field, name, input, isMultiple, tooltip, isVertical, null, isRequired);
			}

			MethodInfo[] methods = GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			foreach (MethodInfo method in methods)
			{
				var customPortBehaviorAttribute = method.GetCustomAttribute<CustomPortBehaviorAttribute>();
				CustomPortBehaviorDelegate behavior = null;

				if (customPortBehaviorAttribute == null)
					continue;

				// Check if custom port behavior function is valid
				try
				{
					Type referenceType = typeof(CustomPortBehaviorDelegate);
					behavior = (CustomPortBehaviorDelegate)Delegate.CreateDelegate(referenceType, this, method, true);
				}
				catch
				{
					Debug.LogError("The function " + method + " cannot be converted to the required delegate format: " + typeof(CustomPortBehaviorDelegate));
				}

				if (nodeFields.TryGetValue(customPortBehaviorAttribute.fieldName, out NodeFieldInformation field))
					field.behavior = behavior;
				else
					Debug.LogError("Invalid field name for custom port behavior: " + method + ", " + customPortBehaviorAttribute.fieldName);
			}
		}

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

		public void OnEdgeDisconnected(SerializableEdge edge)
		{
			if (edge == null)
				return;

			bool input = edge.ToNode == this;
			NodePortContainer portCollection = input ? inputPorts : outputPorts;

			portCollection.Remove(edge);

			// Reset default values of input port:
			if (edge.ToNode != null)
			{
				bool haveConnectedEdges = edge.ToNode.inputPorts.Where(p => p.fieldName == edge.inputFieldName).Any(p => p.GetEdges().Count != 0);
				if (edge.ToNode == this && !haveConnectedEdges && CanResetPort(edge.ToPort))
					edge.ToPort?.ResetToDefault();
			}

			UpdateAllPorts();
		}

		public void OnProcess()
		{
			inputPorts.PullDatas();

			ExceptionToLog.Call(Process);

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
		public void AddPort(bool input, string fieldName, PortData portData)
		{
			// Fixup port data info if needed:
			portData.displayType ??= nodeFields[fieldName].info.FieldType;

			if (input)
				inputPorts.Add(new NodePort(this, fieldName, portData));
			else
				outputPorts.Add(new NodePort(this, fieldName, portData));
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
				inputPorts.RemoveAll(p => p.fieldName == fieldName);
			else
				outputPorts.RemoveAll(p => p.fieldName == fieldName);
		}

		/// <summary>
		/// Get all the nodes connected to the input ports of this node
		/// </summary>
		/// <returns>an enumerable of node</returns>
		public IEnumerable<BaseNode> GetInputNodes()
		{
			foreach (NodePort port in inputPorts)
			foreach (SerializableEdge edge in port.GetEdges())
				yield return edge.FromNode;
		}

		/// <summary>
		/// Get all the nodes connected to the output ports of this node
		/// </summary>
		/// <returns>an enumerable of node</returns>
		public IEnumerable<BaseNode> GetOutputNodes()
		{
			foreach (NodePort port in outputPorts)
			foreach (SerializableEdge edge in port.GetEdges())
				yield return edge.ToNode;
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

				foreach (BaseNode dep in node.GetInputNodes())
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
				bool bothNull = string.IsNullOrEmpty(identifier) && string.IsNullOrEmpty(p.portData.identifier);
				return p.fieldName == fieldName && (bothNull || identifier == p.portData.identifier);
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
				bool bothNull = identifierIsNull && string.IsNullOrEmpty(port.portData.identifier);
				if (!bothNull && identifier != port.portData.identifier) continue;
				foreach (FormerlySerializedAsAttribute attribute in port.fieldInfo.GetCustomAttributes<FormerlySerializedAsAttribute>())
				{
					if (attribute.oldName != fieldName) continue;
					value = port;
					fieldName = port.fieldName;
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
		public IEnumerable<SerializableEdge> GetAllEdges() => AllPorts.SelectMany(port => port.GetEdges());

		/// <summary>
		/// Is the port an input
		/// </summary>
		/// <param name="fieldName"></param>
		/// <returns></returns>
		public bool IsFieldInput(string fieldName) => nodeFields[fieldName].input;

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

		/// <summary>
		/// Set the custom name of the node. This is intended to be used by renamable nodes.
		/// This custom name will be serialized inside the node.
		/// </summary>
		/// <param name="customName">New name of the node.</param>
		public void SetCustomName(string customName) => nodeCustomName = customName;

		/// <summary>
		/// Get the name of the node. If the node have a custom name (set using the UI by double clicking on the node title) then it will return this name first, otherwise it returns the value of the name field.
		/// </summary>
		/// <returns>The name of the node as written in the title</returns>
		public string GetCustomName() => string.IsNullOrEmpty(nodeCustomName) ? name : nodeCustomName;

		#endregion

		public override string ToString() => $"{GetCustomName()} ({GetType().Name}) (in: {graph.name})";
	}
}
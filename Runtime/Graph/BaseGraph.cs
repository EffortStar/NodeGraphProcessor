﻿using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;
using JetBrains.Annotations;
using UnityEditor.Experimental.GraphView;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;

namespace GraphProcessor
{
	public class GraphChanges
	{
		public SerializableEdge removedEdge;
		public SerializableEdge addedEdge;
		public BaseNode removedNode;
		public BaseNode addedNode;
		public BaseNode nodeChanged;
		public Group addedGroups;
		public Group removedGroups;
		public BaseStackNode addedStackNode;
		public BaseStackNode removedStackNode;
		public StickyNote addedStickyNotes;
		public StickyNote removedStickyNotes;
	}

	[Serializable]
	public class BaseGraph : ScriptableObject, ISerializationCallbackReceiver
	{
		/// <summary>
		/// List of all the nodes in the graph.
		/// </summary>
		/// <typeparam name="BaseNode"></typeparam>
		/// <returns></returns>
		[SerializeReference, HideInInspector]
		public List<BaseNode> nodes = new();

		/// <summary>
		/// Dictionary to access node per GUID, faster than a search in a list
		/// </summary>
		/// <typeparam name="string"></typeparam>
		/// <typeparam name="BaseNode"></typeparam>
		/// <returns></returns>
		[NonSerialized]
		public Dictionary<string, BaseNode> nodesPerGUID = new();

		/// <summary>
		/// Json list of edges
		/// </summary>
		/// <typeparam name="SerializableEdge"></typeparam>
		/// <returns></returns>
		[SerializeField, HideInInspector]
		public List<SerializableEdge> edges = new();

		/// <summary>
		/// Dictionary of edges per GUID, faster than a search in a list
		/// </summary>
		/// <typeparam name="string"></typeparam>
		/// <typeparam name="SerializableEdge"></typeparam>
		/// <returns></returns>
		[NonSerialized]
		public Dictionary<string, SerializableEdge> edgesPerGUID = new();

		/// <summary>
		/// All groups in the graph
		/// </summary>
		/// <typeparam name="Group"></typeparam>
		/// <returns></returns>
		[SerializeField, FormerlySerializedAs("commentBlocks"), HideInInspector]
		public List<Group> groups = new();

		/// <summary>
		/// All Stack Nodes in the graph
		/// </summary>
		/// <typeparam name="stackNodes"></typeparam>
		/// <returns></returns>
		[SerializeField, SerializeReference, HideInInspector] // Polymorphic serialization
		public List<BaseStackNode> stackNodes = new();

		/// <summary>
		/// All pinned elements in the graph
		/// </summary>
		/// <typeparam name="PinnedElement"></typeparam>
		/// <returns></returns>
		[SerializeField, HideInInspector]
		public List<PinnedElement> pinnedElements = new();
		
		[SerializeField, HideInInspector]
		internal List<SubgraphParameter> subgraphParameters = new();
		
		/// <summary>
		/// All exposed parameters in the graph.
		/// </summary>
		[PublicAPI]
		public IReadOnlyList<SubgraphParameter> SubgraphParameters => subgraphParameters;

		[SerializeField, HideInInspector]
		public List<StickyNote> stickyNotes = new();
		
		[NonSerialized] private Scene linkedScene;

		// Trick to keep the node inspector alive during the editor session
		[SerializeField, HideInInspector]
		internal UnityEngine.Object nodeInspectorReference;

		/// <summary>
		/// Triggered when something is changed in the list of exposed parameters
		/// </summary>
		public event Action onSubgraphParameterListChanged;

		public event Action<SubgraphParameter> onSubgraphParameterModified;

		/// <summary>
		/// Triggered when the graph is linked to an active scene.
		/// </summary>
		public event Action<Scene> onSceneLinked;

		/// <summary>
		/// Triggered when the graph is enabled
		/// </summary>
		public event Action onEnabled;

		/// <summary>
		/// Triggered when the graph is changed
		/// </summary>
		public event Action<GraphChanges> onGraphChanges;

		[NonSerialized] private bool _isEnabled;

		public bool isEnabled
		{
			get => _isEnabled;
			private set => _isEnabled = value;
		}
		
		protected virtual void OnEnable()
		{
			if (isEnabled)
				OnDisable();

			InitializeGraphElements();
			DestroyBrokenGraphElements();
			isEnabled = true;
			onEnabled?.Invoke();
		}

		private void InitializeGraphElements()
		{
			// Sanitize the element lists (it's possible that nodes are null if their full class name have changed)
			// If you rename / change the assembly of a node or parameter, please use the MovedFrom() attribute to avoid breaking the graph.
			nodes.RemoveAll(n => n == null);
			subgraphParameters.RemoveAll(e => e == null);

			foreach (BaseNode node in nodes.ToList())
			{
				nodesPerGUID[node.GUID] = node;
				node.Initialize(this);
			}

			var requiresReserialization = false;

			foreach (SerializableEdge edge in edges.ToList())
			{
				requiresReserialization |= edge.Deserialize() == SerializableEdge.DeserializationResult.Changed;

				// Sanity check for the edge:
				if (edge.inputPort == null || edge.outputPort == null)
				{
					Disconnect(edge.GUID);
					continue;
				}

				edgesPerGUID[edge.GUID] = edge;

				// Add the edge to the non-serialized port data
				edge.inputPort.owner.OnEdgeConnected(edge);
				edge.outputPort.owner.OnEdgeConnected(edge);
			}

			if (requiresReserialization)
			{
#if UNITY_EDITOR
				UnityEditor.EditorUtility.SetDirty(this);
#endif
			}
		}

		protected virtual void OnDisable()
		{
			isEnabled = false;
			foreach (BaseNode node in nodes)
				node.DisableInternal();
		}

		public virtual void OnAssetDeleted()
		{
		}

		/// <summary>
		/// Adds a node to the graph
		/// </summary>
		/// <param name="node"></param>
		/// <returns></returns>
		public BaseNode AddNode(BaseNode node)
		{
			nodesPerGUID[node.GUID] = node;

			nodes.Add(node);
			node.Initialize(this);

			onGraphChanges?.Invoke(new GraphChanges { addedNode = node });

			return node;
		}

		/// <summary>
		/// Removes a node from the graph
		/// </summary>
		/// <param name="node"></param>
		public void RemoveNode(BaseNode node)
		{
			node.DisableInternal();
			node.DestroyInternal();

			nodesPerGUID.Remove(node.GUID);

			nodes.Remove(node);

			onGraphChanges?.Invoke(new GraphChanges { removedNode = node });
		}

		/// <summary>
		/// Connect two ports with an edge
		/// </summary>
		/// <param name="inputPort">input port</param>
		/// <param name="outputPort">output port</param>
		/// <param name="DisconnectInputs">is the edge allowed to disconnect another edge</param>
		/// <returns>the connecting edge</returns>
		public SerializableEdge Connect(NodePort inputPort, NodePort outputPort, bool autoDisconnectInputs = true)
		{
			var edge = SerializableEdge.CreateNewEdge(this, inputPort, outputPort);

			//If the input port does not support multi-connection, we remove them
			if (autoDisconnectInputs && !inputPort.portData.acceptMultipleEdges)
			{
				foreach (SerializableEdge e in inputPort.GetEdges().ToList())
				{
					// TODO: do not disconnect them if the connected port is the same than the old connected
					Disconnect(e);
				}
			}

			// same for the output port:
			if (autoDisconnectInputs && !outputPort.portData.acceptMultipleEdges)
			{
				foreach (SerializableEdge e in outputPort.GetEdges().ToList())
				{
					// TODO: do not disconnect them if the connected port is the same than the old connected
					Disconnect(e);
				}
			}

			edges.Add(edge);

			// Add the edge to the list of connected edges in the nodes
			inputPort.owner.OnEdgeConnected(edge);
			outputPort.owner.OnEdgeConnected(edge);

			onGraphChanges?.Invoke(new GraphChanges { addedEdge = edge });

			return edge;
		}

		/// <summary>
		/// Disconnect two ports
		/// </summary>
		/// <param name="inputNode">input node</param>
		/// <param name="inputFieldName">input field name</param>
		/// <param name="outputNode">output node</param>
		/// <param name="outputFieldName">output field name</param>
		public void Disconnect(BaseNode inputNode, string inputFieldName, BaseNode outputNode, string outputFieldName)
		{
			edges.RemoveAll(r =>
			{
				bool remove = r.inputNode == inputNode
				              && r.outputNode == outputNode
				              && r.outputFieldName == outputFieldName
				              && r.inputFieldName == inputFieldName;

				if (remove)
				{
					r.inputNode?.OnEdgeDisconnected(r);
					r.outputNode?.OnEdgeDisconnected(r);
					onGraphChanges?.Invoke(new GraphChanges { removedEdge = r });
				}

				return remove;
			});
		}

		/// <summary>
		/// Disconnect an edge
		/// </summary>
		/// <param name="edge"></param>
		public void Disconnect(SerializableEdge edge) => Disconnect(edge.GUID);

		/// <summary>
		/// Disconnect an edge
		/// </summary>
		/// <param name="edgeGUID"></param>
		public void Disconnect(string edgeGUID)
		{
			edges.RemoveAll(r =>
			{
				if (r.GUID == edgeGUID)
				{
					r.inputNode?.OnEdgeDisconnected(r);
					r.outputNode?.OnEdgeDisconnected(r);
					onGraphChanges?.Invoke(new GraphChanges { removedEdge = r });
					return true;
				}

				return false;
			});
		}

		/// <summary>
		/// Add a group
		/// </summary>
		/// <param name="block"></param>
		public void AddGroup(Group block)
		{
			groups.Add(block);
			onGraphChanges?.Invoke(new GraphChanges { addedGroups = block });
		}

		/// <summary>
		/// Removes a group
		/// </summary>
		/// <param name="block"></param>
		public void RemoveGroup(Group block)
		{
			groups.Remove(block);
			onGraphChanges?.Invoke(new GraphChanges { removedGroups = block });
		}

		/// <summary>
		/// Add a StackNode
		/// </summary>
		/// <param name="stackNode"></param>
		public void AddStackNode(BaseStackNode stackNode)
		{
			stackNodes.Add(stackNode);
			onGraphChanges?.Invoke(new GraphChanges { addedStackNode = stackNode });
		}

		/// <summary>
		/// Remove a StackNode
		/// </summary>
		/// <param name="stackNode"></param>
		public void RemoveStackNode(BaseStackNode stackNode)
		{
			stackNodes.Remove(stackNode);
			onGraphChanges?.Invoke(new GraphChanges { removedStackNode = stackNode });
		}

		/// <summary>
		/// Add a sticky note
		/// </summary>
		/// <param name="note"></param>
		public void AddStickyNote(StickyNote note)
		{
			stickyNotes.Add(note);
			onGraphChanges?.Invoke(new GraphChanges { addedStickyNotes = note });
		}

		/// <summary>
		/// Removes a sticky note
		/// </summary>
		/// <param name="note"></param>
		public void RemoveStickyNote(StickyNote note)
		{
			stickyNotes.Remove(note);
			onGraphChanges?.Invoke(new GraphChanges { removedStickyNotes = note });
		}

		/// <summary>
		/// Invoke the onGraphChanges event, can be used as trigger to execute the graph when the content of a node is changed
		/// </summary>
		/// <param name="node"></param>
		public void NotifyNodeChanged(BaseNode node) => onGraphChanges?.Invoke(new GraphChanges { nodeChanged = node });

		/// <summary>
		/// Open a pinned element of type viewType
		/// </summary>
		/// <param name="viewType">type of the pinned element</param>
		/// <returns>the pinned element</returns>
		public PinnedElement OpenPinned(Type viewType)
		{
			PinnedElement pinned = pinnedElements.Find(p => p.editorType.type == viewType);

			if (pinned == null)
			{
				pinned = new PinnedElement(viewType);
				pinnedElements.Add(pinned);
			}
			else
				pinned.opened = true;

			return pinned;
		}

		/// <summary>
		/// Closes a pinned element of type viewType
		/// </summary>
		/// <param name="viewType">type of the pinned element</param>
		public void ClosePinned(Type viewType)
		{
			PinnedElement pinned = pinnedElements.Find(p => p.editorType.type == viewType);
			pinned.opened = false;
		}

		public void OnBeforeSerialize()
		{
			// Cleanup broken elements
			stackNodes.RemoveAll(s => s == null);
			nodes.RemoveAll(n => n == null);
		}

		// We can deserialize data here because it's called in a unity context
		// so we can load objects references
		public void Deserialize()
		{
			// Disable nodes correctly before removing them:
			if (nodes != null)
			{
				foreach (BaseNode node in nodes)
					node.DisableInternal();
			}

			InitializeGraphElements();
		}

		public void OnAfterDeserialize()
		{
		}

		/// <summary>
		/// Add an exposed parameter
		/// </summary>
		/// <returns>The unique id of the parameter</returns>
		public string AddSubgraphParameter(string name, Type type, Direction direction)
		{
			var param = new SubgraphParameter();
			param.Initialize(name, type, direction);
			subgraphParameters.Add(param);
			onSubgraphParameterListChanged?.Invoke();
			return param.Guid;
		}

		/// <summary>
		/// Add an already allocated / initialized parameter to the graph
		/// </summary>
		/// <param name="parameter">The parameter to add</param>
		/// <returns>The unique id of the parameter</returns>
		public string AddSubgraphParameter(SubgraphParameter parameter)
		{
			var guid = Guid.NewGuid().ToString(); // Generated once and unique per parameter

			parameter.Guid = guid;
			subgraphParameters.Add(parameter);

			onSubgraphParameterListChanged?.Invoke();

			return guid;
		}

		/// <summary>
		/// Remove an exposed parameter
		/// </summary>
		/// <param name="ep">the parameter to remove</param>
		public void RemoveSubgraphParameter(SubgraphParameter ep)
		{
			subgraphParameters.Remove(ep);

			onSubgraphParameterListChanged?.Invoke();
		}

		/// <summary>
		/// Remove an exposed parameter
		/// </summary>
		/// <param name="guid">GUID of the parameter</param>
		public void RemoveSubgraphParameter(string guid)
		{
			if (subgraphParameters.RemoveAll(e => e.Guid == guid) != 0)
				onSubgraphParameterListChanged?.Invoke();
		}

		internal void NotifyExposedParameterListChanged()
			=> onSubgraphParameterListChanged?.Invoke();

		/// <summary>
		/// Update the exposed parameter name
		/// </summary>
		/// <param name="parameter">The parameter</param>
		/// <param name="name">new name</param>
		public void UpdateSubgraphParameterName(SubgraphParameter parameter, string name)
		{
			parameter.Name = name;
			onSubgraphParameterModified?.Invoke(parameter);
		}

		/// <summary>
		/// Update parameter visibility
		/// </summary>
		/// <param name="parameter">The parameter</param>
		/// <param name="isHidden">is Hidden</param>
		public void NotifySubgraphParameterChanged(SubgraphParameter parameter) => onSubgraphParameterModified?.Invoke(parameter);

		/// <summary>
		/// Get the exposed parameter from name
		/// </summary>
		/// <param name="name">name</param>
		/// <returns>the parameter or null</returns>
		public SubgraphParameter GetSubgraphParameter(string name) => subgraphParameters.FirstOrDefault(e => e.Name == name);

		/// <summary>
		/// Get exposed parameter from GUID
		/// </summary>
		/// <param name="guid">GUID of the parameter</param>
		/// <returns>The parameter</returns>
		public SubgraphParameter GetSubgraphParameterFromGUID(string guid) => subgraphParameters.FirstOrDefault(e => e?.Guid == guid);

		/// <summary>
		/// Link the current graph to the scene in parameter, allowing the graph to pick and serialize objects from the scene.
		/// </summary>
		/// <param name="scene">Target scene to link</param>
		public void LinkToScene(Scene scene)
		{
			linkedScene = scene;
			onSceneLinked?.Invoke(scene);
		}

		/// <summary>
		/// Return true when the graph is linked to a scene, false otherwise.
		/// </summary>
		public bool IsLinkedToScene() => linkedScene.IsValid();

		/// <summary>
		/// Get the linked scene. If there is no linked scene, it returns an invalid scene
		/// </summary>
		public Scene GetLinkedScene() => linkedScene;

		private void DestroyBrokenGraphElements()
		{
			edges.RemoveAll(e => e.inputNode == null
			                     || e.outputNode == null
			                     || string.IsNullOrEmpty(e.outputFieldName)
			                     || string.IsNullOrEmpty(e.inputFieldName)
			);
			nodes.RemoveAll(n => n == null);
		}

		/// <summary>
		/// Tell if two types can be connected in the context of a graph
		/// </summary>
		/// <param name="t1"></param>
		/// <param name="t2"></param>
		/// <returns></returns>
		public static bool TypesAreConnectable(Type t1, Type t2)
		{
			if (t1 == null || t2 == null)
				return false;

			if (TypeAdapter.AreIncompatible(t1, t2))
				return false;

			//Check if there is custom adapters for this assignation
			if (CustomPortIO.IsAssignable(t1, t2))
				return true;

			//Check for type assignability
			if (t2.IsReallyAssignableFrom(t1))
				return true;

			// User defined type conversions
			if (TypeAdapter.AreAssignable(t1, t2))
				return true;

			return false;
		}
	}
}
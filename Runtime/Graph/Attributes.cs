using System;
using JetBrains.Annotations;
using UnityEngine;

namespace GraphProcessor
{
	/// <summary>
	/// Tell that this field is will generate an input port
	/// </summary>
	[AttributeUsage(AttributeTargets.Field)]
	[MeansImplicitUse]
	public sealed class InputAttribute : Attribute
	{
		public string name;
		public bool allowMultiple;

		/// <summary>
		/// Mark the field as an input port
		/// </summary>
		/// <param name="name">display name</param>
		/// <param name="allowMultiple">is connecting multiple edges allowed</param>
		public InputAttribute(string name = null, bool allowMultiple = false)
		{
			this.name = name;
			this.allowMultiple = allowMultiple;
		}
	}

	/// <summary>
	/// Tell that this field is will generate an output port
	/// </summary>
	[AttributeUsage(AttributeTargets.Field)]
	[MeansImplicitUse]
	public sealed class OutputAttribute : Attribute
	{
		public string name;
		public bool allowMultiple;

		/// <summary>
		/// Mark the field as an output port
		/// </summary>
		/// <param name="name">display name</param>
		/// <param name="allowMultiple">is connecting multiple edges allowed</param>
		public OutputAttribute(string name = null, bool allowMultiple = true)
		{
			this.name = name;
			this.allowMultiple = allowMultiple;
		}
	}

	/// <summary>
	/// Mark the field as a parent to <see cref="OutputAttribute"/> fields.
	/// </summary>
	[AttributeUsage(AttributeTargets.Field)]
	[MeansImplicitUse]
	public sealed class OutputObjectAttribute : Attribute
	{
		public OutputObjectAttribute() { }
	}

	/// <summary>
	/// Mark this port-generating (<see cref="InputAttribute"/>/<see cref="OutputAttribute"/> field as required.
	/// </summary>
	[AttributeUsage(AttributeTargets.Field)]
	public sealed class RequiredPortAttribute : Attribute { }

	/// <summary>
	/// Creates a vertical port instead of the default horizontal one
	/// </summary>
	[AttributeUsage(AttributeTargets.Field)]
	public sealed class VerticalAttribute : Attribute { }

	/// <summary>
	/// Register the node in the NodeProvider class. The node will also be available in the node creation window.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
	public sealed class NodeMenuItemAttribute : Attribute
	{
		public readonly string MenuTitle;
		public Type OnlyCompatibleWithGraph;
		public readonly bool SubgraphSupport;

		/// <summary>
		/// Register the node in the NodeProvider class. The node will also be available in the node creation window.
		/// </summary>
		/// <param name="menuTitle">Path in the menu, use / as folder separators</param>
		public NodeMenuItemAttribute(
			string menuTitle = null,
			Type onlyCompatibleWithGraph = null,
			bool subgraphSupport = true
		)
		{
			MenuTitle = menuTitle;
			OnlyCompatibleWithGraph = onlyCompatibleWithGraph;
			SubgraphSupport = subgraphSupport;
		}
	}

	/// <summary>
	/// Underlying type will change depending on what's assigned to the generic ports.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class)]
	public sealed class GenericNodeAttribute : Attribute
	{
		public Type BaseConstraintType { get; }
		public Type[] ExcludedTypes { get; }
		public string[] Reasons { get; }

		public GenericNodeAttribute(Type baseConstraintType = null)
		{
			BaseConstraintType = baseConstraintType ?? typeof(object);
			ExcludedTypes = Array.Empty<Type>();
			Reasons = Array.Empty<string>();
		}

		public GenericNodeAttribute(Type[] excludedTypes, string[] excludedReasons, Type baseConstraintType = null)
		{
			BaseConstraintType = baseConstraintType ?? typeof(object);
			ExcludedTypes = excludedTypes;
			Reasons = excludedReasons;
		}
	}


	/// <summary>
	/// Register the node in the NodeProvider class. The node will also be available in the node creation window.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
	public sealed class GenericNodeMenuItemAttribute : Attribute
	{
		public readonly string MenuTitle;
		public readonly Type[] TypeParameters;

		/// <summary>
		/// Register the node in the NodeProvider class. The node will also be available in the node creation window.
		/// </summary>
		/// <param name="menuTitle">Path in the menu, use / as folder separators</param>
		/// <param name="typeParameters">The type parameters that create the solid generic node type.</param>
		public GenericNodeMenuItemAttribute(
			string menuTitle = null,
			params Type[] typeParameters
		)
		{
			MenuTitle = menuTitle;
			TypeParameters = typeParameters;
		}
	}

	[AttributeUsage(AttributeTargets.Class)]
	public sealed class NodeColorAttribute : Attribute
	{
		public readonly Color Color;

		public NodeColorAttribute(float r, float g, float b) => Color = new Color(r, g, b);
	}

	/// <summary>
	/// Allow you to modify the generated port view from a field. Can be used to generate multiple ports from one field.<br/>
	/// You must add this attribute on a function of this signature
	/// <code>
	/// IEnumerable&lt;PortData&gt; MyCustomPortFunction();
	/// </code>
	/// </summary>
	[AttributeUsage(AttributeTargets.Method), MeansImplicitUse]
	public sealed class CustomPortBehaviorAttribute : Attribute { }

	/// <summary>
	/// Mark a type as capable of accepting multiple inputs into a relay node.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
	public sealed class MultipleInputsRelayTypeAttribute : Attribute { }

	/// <summary>
	/// Allow you to have a custom view for your stack nodes
	/// </summary>
	[AttributeUsage(AttributeTargets.Class)]
	public sealed class CustomStackNodeViewAttribute : Attribute
	{
		public readonly Type stackNodeType;

		/// <summary>
		/// Allow you to have a custom view for your stack nodes
		/// </summary>
		/// <param name="stackNodeType">The type of the stack node you target</param>
		public CustomStackNodeViewAttribute(Type stackNodeType)
		{
			this.stackNodeType = stackNodeType;
		}
	}

	[AttributeUsage(AttributeTargets.Field)]
	public sealed class VisibleIfAttribute : Attribute
	{
		public readonly string fieldName;
		public readonly object value;

		public VisibleIfAttribute(string fieldName, object value)
		{
			this.fieldName = fieldName;
			this.value = value;
		}
	}

	[AttributeUsage(AttributeTargets.Field)]
	public sealed class ShowInInspectorAttribute : Attribute
	{
		public readonly bool showInNode;

		public ShowInInspectorAttribute(bool showInNode = false)
		{
			this.showInNode = showInNode;
		}
	}

	[AttributeUsage(AttributeTargets.Field)]
	public sealed class ShowAsDrawerAttribute : Attribute { }

	[AttributeUsage(AttributeTargets.Field)]
	public sealed class SettingAttribute : Attribute
	{
		public readonly string name;

		public SettingAttribute(string name = null)
		{
			this.name = name;
		}
	}

	/// <summary>
	/// Mark this node as used for prototyping.
	/// This indicates that the node may undergo unsafe refactoring, and shouldn't be used in a production graph.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class)]
	public sealed class PrototypeNodeAttribute : Attribute { }

	/// <summary>
	/// Adds a hoverable info flag to a node with a text tooltip
	/// </summary>
	[AttributeUsage(AttributeTargets.Class)]
	public sealed class NodeInfoAttribute : Attribute
	{
		public readonly string Message;

		public NodeInfoAttribute(string message)
		{
			Message = message;
		}
	}
}
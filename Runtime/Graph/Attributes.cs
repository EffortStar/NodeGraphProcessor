using System;

namespace GraphProcessor
{
	/// <summary>
	/// Tell that this field is will generate an input port
	/// </summary>
	[AttributeUsage(AttributeTargets.Field)]
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
	/// Mark this port-generating (<see cref="InputAttribute"/>/<see cref="OutputAttribute"/> field as required.
	/// </summary>
	[AttributeUsage(AttributeTargets.Field)]
	public sealed class RequiredPortAttribute : Attribute
	{
	}

	/// <summary>
	/// Creates a vertical port instead of the default horizontal one
	/// </summary>
	[AttributeUsage(AttributeTargets.Field)]
	public sealed class VerticalAttribute : Attribute
	{
	}

	/// <summary>
	/// Register the node in the NodeProvider class. The node will also be available in the node creation window.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
	public sealed class NodeMenuItemAttribute : Attribute
	{
		public string menuTitle;
		public Type onlyCompatibleWithGraph;

		/// <summary>
		/// Register the node in the NodeProvider class. The node will also be available in the node creation window.
		/// </summary>
		/// <param name="menuTitle">Path in the menu, use / as folder separators</param>
		public NodeMenuItemAttribute(string menuTitle = null, Type onlyCompatibleWithGraph = null)
		{
			this.menuTitle = menuTitle;
			this.onlyCompatibleWithGraph = onlyCompatibleWithGraph;
		}
	}

	/// <summary>
	/// Allow you to customize the input function of a port
	/// </summary>
	[AttributeUsage(AttributeTargets.Method)]
	public sealed class CustomPortInputAttribute : Attribute
	{
		public string fieldName;
		public Type inputType;
		public bool allowCast;

		/// <summary>
		/// Allow you to customize the input function of a port.
		/// See CustomPortsNode example in Samples.
		/// </summary>
		/// <param name="fieldName">local field of the node</param>
		/// <param name="inputType">type of input of the port</param>
		/// <param name="allowCast">if cast is allowed when connecting an edge</param>
		public CustomPortInputAttribute(string fieldName, Type inputType, bool allowCast = true)
		{
			this.fieldName = fieldName;
			this.inputType = inputType;
			this.allowCast = allowCast;
		}
	}

	/// <summary>
	/// Allow you to customize the input function of a port
	/// </summary>
	[AttributeUsage(AttributeTargets.Method)]
	public sealed class CustomPortOutputAttribute : Attribute
	{
		public string fieldName;
		public Type outputType;
		public bool allowCast;

		/// <summary>
		/// Allow you to customize the output function of a port.
		/// See CustomPortsNode example in Samples.
		/// </summary>
		/// <param name="fieldName">local field of the node</param>
		/// <param name="outputType">type of output of the port</param>
		/// <param name="allowCast">if cast is allowed when connecting an edge</param>
		public CustomPortOutputAttribute(string fieldName, Type outputType, bool allowCast = true)
		{
			this.fieldName = fieldName;
			this.outputType = outputType;
			this.allowCast = allowCast;
		}
	}

	/// <summary>
	/// Allow you to modify the generated port view from a field. Can be used to generate multiple ports from one field.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method)]
	public sealed class CustomPortBehaviorAttribute : Attribute
	{
		public string fieldName;

		/// <summary>
		/// Allow you to modify the generated port view from a field. Can be used to generate multiple ports from one field.
		/// You must add this attribute on a function of this signature
		/// <code>
		/// IEnumerable&lt;PortData&gt; MyCustomPortFunction(List&lt;SerializableEdge&gt; edges);
		/// </code>
		/// </summary>
		/// <param name="fieldName">local node field name</param>
		public CustomPortBehaviorAttribute(string fieldName)
		{
			this.fieldName = fieldName;
		}
	}

	/// <summary>
	/// Allow you to have a custom view for your stack nodes
	/// </summary>
	[AttributeUsage(AttributeTargets.Class)]
	public sealed class CustomStackNodeViewAttribute : Attribute
	{
		public Type stackNodeType;

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
		public string fieldName;
		public object value;

		public VisibleIfAttribute(string fieldName, object value)
		{
			this.fieldName = fieldName;
			this.value = value;
		}
	}

	[AttributeUsage(AttributeTargets.Field)]
	public sealed class ShowInInspectorAttribute : Attribute
	{
		public bool showInNode;

		public ShowInInspectorAttribute(bool showInNode = false)
		{
			this.showInNode = showInNode;
		}
	}

	[AttributeUsage(AttributeTargets.Field)]
	public sealed class ShowAsDrawerAttribute : Attribute
	{
	}

	[AttributeUsage(AttributeTargets.Field)]
	public sealed class SettingAttribute : Attribute
	{
		public string name;

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
	public sealed class PrototypeNodeAttribute : Attribute
	{
	}
}
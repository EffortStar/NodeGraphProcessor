using System;
using JetBrains.Annotations;

namespace GraphProcessor
{
	[AttributeUsage(AttributeTargets.Class)]
	public sealed class NodeCustomEditorAttribute : Attribute
	{
		public Type nodeType;

		public NodeCustomEditorAttribute(Type nodeType)
		{
			this.nodeType = nodeType;
		}
	}
	
	/// <summary>
	/// Decorates a static method that returns <see cref="ProducedNode"/>[] to automatically generate node menu items.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method)]
	public sealed class NodeMenuItemProducerAttribute : Attribute { }

	public readonly struct ProducedNode
	{
		internal readonly string MenuTitle;
		internal readonly Type NodeType;
		internal readonly Action<BaseNode> Configure;

		/// <summary>
		/// Register the node in the NodeProvider class. The node will also be available in the node creation window.
		/// </summary>
		/// <param name="nodeType">The type of node to produce.</param>
		/// <param name="menuTitle">Path in the menu, use / as folder separators.</param>
		/// <param name="configure">Code run when creating the node via <paramref name="menuTitle"/>.</param>
		public ProducedNode(Type nodeType, string menuTitle, Action<BaseNode> configure)
		{
			NodeType = nodeType;
			MenuTitle = menuTitle;
			Configure = configure;
		}
	}
}
using System;

namespace GraphProcessor
{
	[AttributeUsage(AttributeTargets.Class)]
	public class NodeCustomEditorAttribute : Attribute
	{
		public Type nodeType;

		public NodeCustomEditorAttribute(Type nodeType)
		{
			this.nodeType = nodeType;
		}
	}
}
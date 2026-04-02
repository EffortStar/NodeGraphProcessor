using System;
using System.Collections.Generic;
using UnityEngine;

namespace GraphProcessor
{
	[Serializable, NodeMenuItem("Operation/Equals")]
	[GenericNode(
		new[] { typeof(float) },
		new[] { "floats should be compared using greater/less than, not strict equality" }
	)]
	public class EqualNode<T> : BaseNode
	{
		[Input(name = "A"), SerializeField]
		public T InputA;

		[Input(name = "B"), SerializeField]
		public T InputB;

		[Output(name = "")]
		public bool Equal;

		public override string name => $"{TypeUtility.FormatTypeName(typeof(T), nicify: true)} ==";

		protected override void Process() => Equal = EqualityComparer<T>.Default.Equals(InputA, InputB);
	}
}
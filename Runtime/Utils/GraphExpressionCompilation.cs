using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace GraphProcessor
{
	public readonly struct EdgeKey : IEquatable<EdgeKey>
	{
		public readonly FieldInfo FromRoot;
		public readonly FieldInfo FromLeaf;
		public readonly FieldInfo ToRoot;
		public readonly FieldInfo ToLeaf;
		private readonly int _hashCode;

		public EdgeKey(
			NodeFieldInformation from,
			NodeFieldInformation to
		)
		{
			FromRoot = from.Path.Root.FieldInfo;
			ToRoot = to.Path.Root.FieldInfo;
			FromLeaf = from.Path.FieldInfo;
			ToLeaf = to.Path.FieldInfo;
			_hashCode = HashCode.Combine(FromRoot, ToRoot, FromLeaf, ToLeaf);
		}

		public bool Equals(EdgeKey other) =>
			FromRoot.Equals(other.FromRoot)
			&& ToRoot.Equals(other.ToRoot)
			&& FromLeaf.Equals(other.FromLeaf)
			&& ToLeaf.Equals(other.ToLeaf);

		public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);

		public override int GetHashCode() => _hashCode;

		public static bool operator ==(EdgeKey left, EdgeKey right) => left.Equals(right);

		public static bool operator !=(EdgeKey left, EdgeKey right) => !left.Equals(right);
	}

	public static class GraphExpressionCompilation
	{
		/// <summary>
		/// Delegate to send the data from this port to another port connected by an edge.
		/// </summary>
		public delegate void PushDataDelegate(BaseNode from, BaseNode to);

		private static readonly Dictionary<EdgeKey, PushDataDelegate> s_pushDataDelegates = new();
		
		public static PushDataDelegate GetPushDataDelegate(SerializableEdge edge, UnityEngine.Object context)
		{
			if (edge.Key is not { } edgeKey)
			{
				Debug.LogError($"[NodeGraph] Edges generated with {nameof(CustomPortBehaviorAttribute)} cannot be executed and must be removed from the graph during Realization. ({context})");
				return null;
			}

			if (s_pushDataDelegates.TryGetValue(edgeKey, out var edgeDelegate))
			{
				return edgeDelegate;
			}

			try
			{
				edgeDelegate = CreatePushDataExpression(edge).Compile();
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				return null;
			}

			s_pushDataDelegates.Add(edgeKey, edgeDelegate);
			return edgeDelegate;
		}

		private static readonly ParameterExpression[] s_params = new ParameterExpression[2];

		public static Expression<PushDataDelegate> CreatePushDataExpression(SerializableEdge edge)
		{
			NodeFieldInformation fromFieldInfo = edge.FromPort._fieldInfo;
			NodeFieldInformation toFieldInfo = edge.ToPort._fieldInfo;

			// We keep slow checks inside the editor
#if UNITY_EDITOR
			if (!BaseGraph.TypesAreConnectable(fromFieldInfo.FieldType, toFieldInfo.FieldType))
			{
				Debug.LogError($"[NodeGraph] Can't convert from {fromFieldInfo.FieldType} to {toFieldInfo.FieldType}, " +
					"you must specify a custom port function (i.e CustomPortInput or CustomPortOutput) for non-implicit conversions. " +
					$" {edge.FromNode} -> {edge.ToNode}"
				);
				return null;
			}
#endif

			if (toFieldInfo.Path.Parent != null)
			{
				// NOTE: This says "OutputObjectAttribute", because there isn't an InputObjectAttribute yet.
				Debug.LogError($"[NodeProcessor] {nameof(OutputObjectAttribute)} is not yet supported on input ports.");
				return null;
			}

			// Take the node in as a parameter.
			ParameterExpression fromParam = Expression.Parameter(typeof(BaseNode), "from");
			ParameterExpression toParam = Expression.Parameter(typeof(BaseNode), "to");
			s_params[0] = fromParam;
			s_params[1] = toParam;

			// Convert the parameter to their real types.
			NodeFieldPath fromLeaf = fromFieldInfo.Path;
			NodeFieldPath fromRoot = fromLeaf.Root;
			UnaryExpression fromConverted = Expression.Convert(fromParam, fromRoot.FieldInfo.DeclaringType!);

			Expression fromParamField;
			if (fromLeaf != fromRoot)
			{
				Stack<NodeFieldPath> pathStack = new();
				for (NodeFieldPath from = fromLeaf; from != null; from = from.Parent)
				{
					pathStack.Push(from);
				}

				fromParamField = fromConverted;
				while (pathStack.TryPop(out NodeFieldPath path))
				{
					fromParamField = Expression.Field(fromParamField, path.FieldInfo);
				}
			}
			else
			{
				fromParamField = Expression.Field(fromConverted, fromLeaf.FieldInfo);
			}

			Type fromType = edge.FromPort.DisplayType ?? fromFieldInfo.FieldType;

			UnaryExpression toConverted = Expression.Convert(toParam, toFieldInfo.Path.FieldInfo.DeclaringType!);
			Expression toParamField = Expression.Field(toConverted, toFieldInfo.Path.FieldInfo);
			Type toType = edge.ToPort.DisplayType ?? toFieldInfo.FieldType;

			if (fromType != toType)
			{
				// If there is a user defined conversion function, then we call it
				if (TypeAdapter.AreAssignable(fromType, toType))
				{
					// We add a cast in case there we're calling the conversion method with a base class parameter (like object)
					UnaryExpression convertedParam = Expression.Convert(fromParamField, fromType);
					fromParamField = Expression.Call(TypeAdapter.GetConversionMethod(fromType, toType), convertedParam);
					// In case there is a custom port behavior in the output, then we need to re-cast to the base type because
					// the conversion method return type is not always assignable directly:
					fromParamField = Expression.Convert(fromParamField, toFieldInfo.FieldType);
				}
				else // otherwise we cast
				{
					fromParamField = Expression.Convert(fromParamField, toFieldInfo.FieldType);
				}
			}

			BinaryExpression assign = Expression.Assign(toParamField, fromParamField);
			return Expression.Lambda<PushDataDelegate>(assign, s_params);
		}
	}
}
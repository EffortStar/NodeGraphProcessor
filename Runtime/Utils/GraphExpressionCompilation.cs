using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using UnityEngine;

namespace GraphProcessor
{
	public static class GraphExpressionCompilation
	{
		/// <summary>
		/// Delegate to send the data from this port to another port connected by an edge.
		/// </summary>
		public delegate void PushDataDelegate(BaseNode from, BaseNode to);

		private static readonly Dictionary<string, PushDataDelegate> s_pushDataDelegates = new();
		private static bool s_logOnce;
		
		public static PushDataDelegate GetPushDataDelegate(SerializableEdge edge, UnityEngine.Object context)
		{
			string edgeKey = edge.Key;
			if (edgeKey == null)
			{
				Debug.LogError($"[NodeGraph] Edges generated with {nameof(CustomPortBehaviorAttribute)} cannot be executed and must be removed from the graph during Realization. ({context})");
				return null;
			}
			
			// NOTE: In the editor this is just a stub.
			//  Packages/com.alelievr.node-graph-processor/Runtime/Plugins/Game.Graphs.Compiled.dll
			//  is Editor-only, and 'GraphCompilation' is used at build-time to generate the actual assembly used
			//  in builds. The behaviour seen in the editor is used as a fallback.
			PushDataDelegate @delegate = StaticEdgePushFunctions.Get(edgeKey);
			if (@delegate != null)
			{
				return @delegate;
			}
			
#if !UNITY_EDITOR && DEBUG
			Debug.Log($"[Graph] An edge {nameof(PushDataDelegate)} didn't use a static function.\n{edge.Key}");
			if (!s_logOnce)
			{
				Debug.Log($"\n[Graph] Start edge keys:\n\n");
				StaticEdgePushFunctions.Log();
				Debug.Log($"\n[Graph] End edge keys.\n");
				s_logOnce = true;
			}
#endif

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
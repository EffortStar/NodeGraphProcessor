using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using JetBrains.Annotations;
using UnityEngine;
using static GraphProcessor.GraphExpressionCompilation;
using Object = UnityEngine.Object;

namespace GraphProcessor
{
	/// <summary>
	/// Compiles edge push functions for our graphs so we're not executing interpreted Linq expressions.
	/// </summary>
	public sealed class GraphCompilation
	{
		private readonly Dictionary<string, SerializableEdge> _edges = new();
		private readonly int _graphCount;
		private readonly bool _debug;

		public GraphCompilation(IEnumerable<BaseGraph> graphs, bool debug = false)
		{
			_debug = debug;
			_graphCount = 0;
			foreach (BaseGraph graphPrefab in graphs)
			{
				// Don't compile subgraphs.
				// Their edges are realized into the final graphs.
				if (graphPrefab.IsSubgraph)
				{
					continue;
				}

				BaseGraph graph = Object.Instantiate(graphPrefab);
				try
				{
					graph.name = graphPrefab.name;
					// NOTE: We have to realize graphs to make sure edge connections
					//  between subgraphs and relays are resolved to their final form.
					//  Otherwise those graphs would contain edges that would resolve
					//  to Linq Expressions.
					graph.Realize();

					foreach (SerializableEdge edge in graph.edges)
					{
						if (edge.Key is { } key)
						{
							_edges.TryAdd(key, edge);
						}
					}

				}
				finally
				{
					Object.DestroyImmediate(graph);
				}

				_graphCount++;
			}
		}

		[MustUseReturnValue]
		public AssemblyBuilder Compile()
		{
			Debug.Log($"Compiling {_edges.Count} FieldInfo->FieldInfo edges across {_graphCount} graphs.");

			// Create assembly
			const string assemblyName = "Game.Graphs.Compiled";
			AssemblyBuilder builder = AssemblyBuilder.DefineDynamicAssembly(
				new AssemblyName(assemblyName),
				AssemblyBuilderAccess.RunAndSave
			);
			ModuleBuilder module = builder.DefineDynamicModule($"{assemblyName}.dll");

			// Create type
			TypeBuilder typeBuilder = module.DefineType(
				"StaticEdgePushFunctions",
				TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class | TypeAttributes.Abstract
			);

			// Create fields
			FieldBuilder pushDelegatesField = typeBuilder.DefineField(
				"s_pushDelegates",
				typeof(Dictionary<string, PushDataDelegate>),
				FieldAttributes.Static | FieldAttributes.Private
			);

			// Create Push methods.
			Dictionary<string, MethodBuilder> keyToMethod = new();
			foreach ((string key, SerializableEdge edge) in _edges)
			{
				Expression<PushDataDelegate> expression
					= CreatePushDataExpression(edge);

				NodeFieldPath from = edge.FromPort._fieldInfo.Path;
				NodeFieldPath to = edge.ToPort._fieldInfo.Path;
				string methodName = "Push_" +
					$"{GetTypeName(from.FieldInfo.DeclaringType)}_{from.FieldPath.Replace('.', '_')}" +
					"_To_" +
					$"{GetTypeName(to.FieldInfo.DeclaringType)}_{to.FieldPath.Replace('.', '_')}";


				MethodBuilder method = typeBuilder.DefineMethod(
					methodName,
					MethodAttributes.Private | MethodAttributes.Static,
					typeof(void),
					new[] { typeof(BaseNode), typeof(BaseNode) }
				);

				expression.CompileToMethod(method);
				keyToMethod.Add(key, method);
			}

			// Create Constructor
			ConstructorBuilder constructor = typeBuilder.DefineConstructor(
				MethodAttributes.Static | MethodAttributes.Private,
				CallingConventions.Standard,
				Array.Empty<Type>()
			);
			CreateConstructor(constructor);

			// Create Get
			MethodBuilder getMethod = typeBuilder.DefineMethod(
				"Get",
				MethodAttributes.Public | MethodAttributes.Static,
				typeof(void),
				new[] { typeof(string) }
			);
			CreateGetMethod(getMethod);

			if (_debug)
			{
				MethodBuilder logMethod = typeBuilder.DefineMethod(
					"Log",
					MethodAttributes.Public | MethodAttributes.Static,
					typeof(void),
					Array.Empty<Type>()
				);
				CreateLogMethod(logMethod);
			}

			typeBuilder.CreateType();

			return builder;

			static string GetTypeName(Type type)
			{
				if (!type.IsGenericType)
				{
					return type.Name;
				}

				string name = type.Name;
				int iBacktick = name.IndexOf('`');
				if (iBacktick > 0)
				{
					name = name.Remove(iBacktick);
				}

				name += "<";
				Type[] typeParameters = type.GetGenericArguments();
				for (var i = 0; i < typeParameters.Length; ++i)
				{
					string typeParamName = GetTypeName(typeParameters[i]);
					name += i == 0 ? typeParamName : "_" + typeParamName;
				}

				name += ">";

				return name;
			}

			void CreateConstructor(ConstructorBuilder constructorBuilder)
			{
				// https://sharplab.io/#v2:EYLgHgbALANALiAlgGwD4AEBMBGAsAKHQAYACdbKAbgPQGYzsIzMSBhEgbwJJ7PoBMApskEBzAIZxBZKCQAKAVwDOACwAik8WuFjJggBQB7YACtBAYzgkAZgCdDAWxgljZyyTiGAlNXy8+DExqiJaIhgB24rYAngA85ETOiqoacFo6ElIAfCRKAPoADsrqGXpKvv7cvORMrPpenFX+PPlFKaVSSiQAvCThggDu+thERD5Nza3F2iKZgkoAdACC/Pz6AETYmLQj20Rb684AomDiDgUiYxXNLYXTHfPLqxtbO0R7FIckJ2cXgtjjPy8AC+Exu4IhvAmdECMm+p3OlyMpgsVjsjmcrlRHm8nFBQJ4MJqcJ+iP+yLcaPsThcKPcngaHHxwKAA===
				MethodInfo addMethod = typeof(Dictionary<string, PushDataDelegate>).GetMethod(
					nameof(Dictionary<string, PushDataDelegate>.Add),
					BindingFlags.Public | BindingFlags.Instance
				)!;

				ILGenerator il = constructorBuilder.GetILGenerator();
				// s_pushDelegates = new(capacity)
				il.Emit(OpCodes.Ldc_I4, keyToMethod.Count); // NOTE _S is for short ints.
				il.Emit(OpCodes.Newobj, typeof(Dictionary<string, PushDataDelegate>).GetConstructor(new[] { typeof(int) })!);
				il.Emit(OpCodes.Stsfld, pushDelegatesField);
				// s_pushDelegates.Add(key, (GraphExpressionCompilation.PushDataDelegate) method);
				// [repeat]
				ConstructorInfo delegateConstructor = typeof(PushDataDelegate).GetConstructors(
					BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public
				)[0];
				foreach ((string key, MethodBuilder method) in keyToMethod)
				{
					il.Emit(OpCodes.Ldsfld, pushDelegatesField);
					il.Emit(OpCodes.Ldstr, key);
					il.Emit(OpCodes.Ldnull);
					il.Emit(OpCodes.Ldftn, method);
					il.Emit(OpCodes.Newobj, delegateConstructor);
					il.Emit(OpCodes.Callvirt, addMethod);
				}

				il.Emit(OpCodes.Ret);
			}

			void CreateGetMethod(MethodBuilder method)
			{
				/*
				  public static GraphExpressionCompilation.PushDataDelegate Get(string key)
				  {
				    GraphExpressionCompilation.PushDataDelegate pushDataDelegate;
				    return StaticEdgePushFunctions.s_pushDelegates.TryGetValue(key, out pushDataDelegate) ? pushDataDelegate : (GraphExpressionCompilation.PushDataDelegate) null;
				  }
				 */

				MemberExpression pushDelegateExpression = Expression.MakeMemberAccess(null, pushDelegatesField);

				ParameterExpression keyParameter = Expression.Parameter(typeof(string), "key");
				ParameterExpression outValue = Expression.Variable(typeof(PushDataDelegate), "value");

				ConditionalExpression body = Expression.Condition(
					test:
					Expression.Call(
						pushDelegateExpression,
						typeof(Dictionary<string, PushDataDelegate>).GetMethod(
							nameof(Dictionary<string, PushDataDelegate>.TryGetValue),
							BindingFlags.Public | BindingFlags.Instance
						)!,
						keyParameter,
						outValue
					),
					ifTrue: outValue,
					ifFalse: Expression.Convert(Expression.Constant(null), typeof(PushDataDelegate))
				);

				Expression.Lambda(
						Expression.Block(
							variables: new[] { outValue },
							body
						),
						keyParameter
					)
					.CompileToMethod(method);
			}

			void CreateLogMethod(MethodBuilder method)
			{
				MemberExpression pushDelegateExpression = Expression.MakeMemberAccess(null, pushDelegatesField);
				var keysGetMethod = typeof(Dictionary<string, PushDataDelegate>)
					.GetProperty(nameof(Dictionary<string, PushDataDelegate>.Keys), BindingFlags.Instance | BindingFlags.Public)!
					.GetGetMethod();

				var collection = Expression.Parameter(typeof(Dictionary<string, PushDataDelegate>.KeyCollection), "collection");
				var loopVar = Expression.Parameter(typeof(string), "loopVar");
				var loopBody = Expression.Call(typeof(Console).GetMethod(nameof(Console.WriteLine), new[] { typeof(string) })!, loopVar);
				var loop = ForEach(collection, loopVar, loopBody);

				var loopLambda = Expression.Lambda(loop, collection);

				var body = Expression.Block(
					variables: new[] { collection },
					Expression.Assign(collection, Expression.Call(pushDelegateExpression, keysGetMethod)),
					Expression.Invoke(loopLambda, collection)
				);

				Expression.Lambda(body).CompileToMethod(method);
				return;

				// https://stackoverflow.com/questions/27175558/foreach-loop-using-expression-trees
				static Expression ForEach(Expression collection, ParameterExpression loopVar, Expression loopContent)
				{
					var elementType = loopVar.Type;
					var enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);
					var enumeratorType = typeof(IEnumerator<>).MakeGenericType(elementType);

					var enumeratorVar = Expression.Variable(enumeratorType, "enumerator");
					var getEnumeratorCall = Expression.Call(collection, enumerableType.GetMethod("GetEnumerator")!);
					var enumeratorAssign = Expression.Assign(enumeratorVar, getEnumeratorCall);

					// The MoveNext method's actually on IEnumerator, not IEnumerator<T>
					var moveNextCall = Expression.Call(enumeratorVar, typeof(IEnumerator).GetMethod(nameof(IEnumerator.MoveNext))!);

					var breakLabel = Expression.Label("LoopBreak");

					var loop = Expression.Block(new[] { enumeratorVar },
						enumeratorAssign,
						Expression.Loop(
							Expression.IfThenElse(
								Expression.Equal(moveNextCall, Expression.Constant(true)),
								Expression.Block(new[] { loopVar },
									Expression.Assign(loopVar, Expression.Property(enumeratorVar, nameof(IEnumerator.Current))),
									loopContent
								),
								Expression.Break(breakLabel)
							),
							breakLabel
						)
					);

					return loop;
				}
			}
		}
	}
}
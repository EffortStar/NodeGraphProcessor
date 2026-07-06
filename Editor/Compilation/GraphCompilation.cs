using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using static GraphProcessor.GraphExpressionCompilation;

namespace GraphProcessor
{
	public class ProcessGraphsForBuild : IPreprocessBuildWithContext
	{
		public int callbackOrder => 0;

		public void OnPreprocessBuild(BuildCallbackContext ctx)
		{
			Debug.Log(nameof(ProcessGraphsForBuild));
			CompileAllGraphs();
		}

		[MenuItem("Effort Star/Graphs/Compile Edge Push Functions")]
		public static void CompileAllGraphs()
		{
			GraphCompilation compilation = new(
				AssetDatabase.FindAssets("t:" + nameof(BaseGraph), new[] { "Assets" })
					.Select(guid => AssetDatabase.LoadAssetAtPath<BaseGraph>(AssetDatabase.GUIDToAssetPath(guid)))
					.Where(graph => graph != null)
			);
			compilation.Compile();
		}
	}

	public sealed class GraphCompilation
	{
		private readonly Dictionary<EdgeKey, SerializableEdge> _edges = new();
		private readonly int _graphCount;

		public GraphCompilation(IEnumerable<BaseGraph> graphs)
		{
			_graphCount = 0;
			foreach (BaseGraph graph in graphs)
			{
				// Don't compile subgraphs.
				if (graph.IsSubgraph)
				{
					continue;
				}

				foreach (SerializableEdge edge in graph.edges)
				{
					if (edge.Key is { } key)
					{
						_edges.TryAdd(key, edge);
					}
				}

				_graphCount++;
			}
		}

		public void Compile()
		{
			Debug.Log($"Compiling {_edges.Count} FieldInfo->FieldInfo edges across {_graphCount} graphs.");

			const string assemblyName = "Game.Graphs.Compiled";
			AssemblyBuilder builder = AssemblyBuilder.DefineDynamicAssembly(
				new AssemblyName(assemblyName),
				AssemblyBuilderAccess.RunAndSave
			);
			ModuleBuilder module = builder.DefineDynamicModule($"{assemblyName}.dll");

			TypeBuilder typeBuilder = module.DefineType(
				"StaticEdgePushFunctions",
				TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class | TypeAttributes.Abstract
			);

			Dictionary<int, MethodBuilder> keyToMethod = new();

			FieldBuilder pushDelegatesField = typeBuilder.DefineField(
				"s_pushDelegates",
				typeof(Dictionary<int, PushDataDelegate>),
				FieldAttributes.Static | FieldAttributes.Private
			);

			foreach ((EdgeKey key, SerializableEdge edge) in _edges)
			{
				Expression<PushDataDelegate> expression
					= CreatePushDataExpression(edge);

				NodeFieldPath from = edge.FromPort._fieldInfo.Path;
				NodeFieldPath to = edge.ToPort._fieldInfo.Path;
				string methodName = "Push_" +
					$"<{GetTypeName(from.FieldInfo.DeclaringType)}_{from.FieldPath.Replace('.', '_')}>" +
					"_To_" +
					$"<{GetTypeName(to.FieldInfo.DeclaringType)}_{to.FieldPath.Replace('.', '_')}>";


				MethodBuilder method = typeBuilder.DefineMethod(
					methodName,
					MethodAttributes.Private | MethodAttributes.Static,
					typeof(void),
					new[] { typeof(BaseNode), typeof(BaseNode) }
				);

				expression.CompileToMethod(method);
				keyToMethod.Add(key.GetHashCode(), method);
			}

			/*BlockExpression delegateLookupBlock = CreatePushDataDelegateLookup();
			ConstructorBuilder constructor = typeBuilder.DefineConstructor(
				MethodAttributes.Static | MethodAttributes.Private,
				CallingConventions.Standard,
				Array.Empty<Type>()
			);
			Expression.Lambda(delegateLookupBlock).CompileToMethod(
				constructor
			);*/

			MethodBuilder getMethod = typeBuilder.DefineMethod(
				"Get",
				MethodAttributes.Public | MethodAttributes.Static,
				typeof(void),
				new[] { typeof(EdgeKey) }
			);
			CreateGetMethod(getMethod);

			typeBuilder.CreateType();

			string assemblyFileName = $"{builder.GetName().Name}.dll";
			builder.Save(assemblyFileName);
			string destinationPath = Path.GetFullPath(Path.Combine("Library", "ScriptAssemblies", assemblyFileName));
			File.Delete(destinationPath);
			File.Move(assemblyFileName, destinationPath);
			Debug.Log($"Finished compiling to \"{destinationPath}\".");
			return;

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

			BlockExpression CreatePushDataDelegateLookup()
			{
				// TODO figure out how to assign this
				MemberExpression pushDelegateExpression = Expression.MakeMemberAccess(null, pushDelegatesField);
				return Expression.Block(
					keyToMethod.Select(args =>
						{
							(int key, var method) = args;
							ConstantExpression keyConst = Expression.Constant(key, typeof(int));
							UnaryExpression @delegate = Expression.Convert(Expression.MakeMemberAccess(null, method), typeof(PushDataDelegate));
							return Expression.Call(pushDelegateExpression,
								typeof(Dictionary<int, PushDataDelegate>).GetMethod(
									nameof(Dictionary<int, PushDataDelegate>.Add),
									BindingFlags.Public | BindingFlags.Instance
								)!,
								keyConst,
								@delegate
							);
						}
					)
				);
			}

			void CreateGetMethod(MethodBuilder getMethod)
			{
				/*
				  public static GraphExpressionCompilation.PushDataDelegate Get(int key)
				  {
				    GraphExpressionCompilation.PushDataDelegate pushDataDelegate;
				    return StaticEdgePushFunctions.s_pushDelegates.TryGetValue(key, out pushDataDelegate) ? pushDataDelegate : (GraphExpressionCompilation.PushDataDelegate) null;
				  }
				 */

				MemberExpression pushDelegateExpression = Expression.MakeMemberAccess(null, pushDelegatesField);

				ParameterExpression keyParameter = Expression.Parameter(typeof(int), "key");
				ParameterExpression outValue = Expression.Variable(typeof(PushDataDelegate), "value");

				ConditionalExpression body = Expression.Condition(
					test:
					Expression.Call(
						pushDelegateExpression,
						typeof(Dictionary<int, PushDataDelegate>).GetMethod(
							nameof(Dictionary<int, PushDataDelegate>.TryGetValue),
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
					.CompileToMethod(getMethod);
			}
		}
	}
}
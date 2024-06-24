using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace GraphProcessor
{
	/// <summary>
	/// Implement this interface to use the inside your class to define type conversions to use inside the graph.
	/// Example:
	/// <code>
	/// public class CustomConversions : ITypeAdapter
	/// {
	///     public static Vector4 ConvertFloatToVector(float from) => new Vector4(from, from, from, from);
	///     ...
	/// }
	/// </code>
	/// </summary>
	public interface ITypeAdapter
	{
		IEnumerable<(Type, Type)> GetIncompatibleTypes()
		{
			yield break;
		}
	}

	public static class TypeAdapter
	{
		private static readonly Dictionary<(Type from, Type to), Func<object, object>> adapters = new();
		private static readonly Dictionary<(Type from, Type to), MethodInfo> adapterMethods = new();
		private static readonly List<(Type from, Type to)> incompatibleTypes = new();

		[NonSerialized] private static bool adaptersLoaded;

#if !ENABLE_IL2CPP
		static Func<object, object> ConvertTypeMethodHelper<TParam, TReturn>(MethodInfo method)
		{
			// Convert the slow MethodInfo into a fast, strongly typed, open delegate
			var func = (Func<TParam, TReturn>)Delegate.CreateDelegate
				(typeof(Func<TParam, TReturn>), method);

			// Now create a more weakly typed delegate which will call the strongly typed one
			Func<object, object> ret = param => func((TParam)param);
			return ret;
		}
#endif

		private static void LoadAllAdapters()
		{
#if UNITY_EDITOR
			foreach (Type type in UnityEditor.TypeCache.GetTypesDerivedFrom<ITypeAdapter>())
			{
#else
			foreach (Type type in AppDomain.CurrentDomain.GetAllTypes())
			{
				if (!typeof(ITypeAdapter).IsAssignableFrom(type))
					continue;
				
#endif
				if (type.IsAbstract)
					continue;

				var adapter = (ITypeAdapter)Activator.CreateInstance(type);
				if (adapter != null)
				{
					foreach ((Type, Type) types in adapter.GetIncompatibleTypes())
					{
						incompatibleTypes.Add((types.Item1, types.Item2));
						incompatibleTypes.Add((types.Item2, types.Item1));
					}
				}

				foreach (MethodInfo method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
				{
					if (method.GetParameters().Length != 1)
					{
						Debug.LogError($"Ignoring conversion method {method} because it does not have exactly one parameter");
						continue;
					}

					if (method.ReturnType == typeof(void))
					{
						Debug.LogError($"Ignoring conversion method {method} because it does not returns anything");
						continue;
					}

					Type from = method.GetParameters()[0].ParameterType;
					Type to = method.ReturnType;

					try
					{
#if ENABLE_IL2CPP
						// IL2CPP doesn't support calling generic functions via reflection (AOT can't generate templated code)
						Func<object, object> r = (object param) => { return (object)method.Invoke(null, new object[]{ param }); };
#else
						MethodInfo genericHelper = typeof(TypeAdapter).GetMethod(nameof(ConvertTypeMethodHelper),
							BindingFlags.Static | BindingFlags.NonPublic)!;

						// Now supply the type arguments
						MethodInfo constructedHelper = genericHelper.MakeGenericMethod(from, to);

						object ret = constructedHelper.Invoke(null, new object[] { method });
						var r = (Func<object, object>)ret;
#endif

						adapters.Add((method.GetParameters()[0].ParameterType, method.ReturnType), r);
						adapterMethods.Add((method.GetParameters()[0].ParameterType, method.ReturnType), method);
					}
					catch (Exception e)
					{
						Debug.LogError($"Failed to load the type conversion method: {method}\n{e}");
					}
				}
			}

			adaptersLoaded = true;
		}

		public static bool AreIncompatible(Type from, Type to) => incompatibleTypes.Any(k => k.from == from && k.to == to);

		public static bool AreAssignable(Type from, Type to)
		{
			if (!adaptersLoaded)
				LoadAllAdapters();

			if (AreIncompatible(from, to))
				return false;

			return adapters.ContainsKey((from, to));
		}

		public static MethodInfo GetConversionMethod(Type from, Type to) => adapterMethods[(from, to)];

		public static object Convert(object from, Type targetType)
		{
			if (!adaptersLoaded)
				LoadAllAdapters();

			return adapters.TryGetValue((from.GetType(), targetType), out Func<object, object> conversionFunction)
				? conversionFunction?.Invoke(from)
				: null;
		}
	}
}
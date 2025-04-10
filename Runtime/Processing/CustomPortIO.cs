using System;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using System.Linq.Expressions;

namespace GraphProcessor
{
	public delegate void CustomPortIODelegate(BaseNode node, List< SerializableEdge > edges, NodePort outputPort = null);

	public static class CustomPortIO
	{
		class PortIOPerField : Dictionary< string, CustomPortIODelegate > {}
		class PortIOPerNode : Dictionary< Type, PortIOPerField > {}

		static Dictionary< Type, List< Type > >	assignableTypes = new();
		static PortIOPerNode					customIOPortMethods = new();

		static CustomPortIO()
		{
			LoadCustomPortMethods();
		}

		static void LoadCustomPortMethods()
		{
			BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

			Type portInputType = typeof(CustomPortInputAttribute);
			Type portOutputType = typeof(CustomPortOutputAttribute);

#if UNITY_EDITOR
			foreach (var type in UnityEditor.TypeCache.GetTypesDerivedFrom<BaseNode>())
			{
				if (type.IsAbstract || type.ContainsGenericParameters)
					continue;
#else
			foreach (var type in AppDomain.CurrentDomain.GetAllTypes())
			{
				if (type.IsAbstract || type.ContainsGenericParameters)
					continue;
				if (!type.IsSubclassOf(typeof(BaseNode)))
					continue;
#endif

				var methods = type.GetMethods(bindingFlags);

				foreach (var method in methods)
				{
					
					if (!Attribute.IsDefined(method, portInputType) && !Attribute.IsDefined(method, portOutputType))
						continue;
					
					var p = method.GetParameters();
					// Check if the function can take a NodePort in optional param
					bool nodePortSignature = p.Length == 2 && p[1].ParameterType == typeof(NodePort);


					CustomPortIODelegate deleg;
#if ENABLE_IL2CPP
					// IL2CPP doesn't support expression builders
					if (nodePortSignature)
					{
						deleg = new CustomPortIODelegate((node, edges, port) => {
							method.Invoke(node, new object[]{ edges, port});
						});
					}
					else
					{
						deleg = new CustomPortIODelegate((node, edges, port) => {
							method.Invoke(node, new object[]{ edges });
						});
					}
#else
					var p1 = Expression.Parameter(typeof(BaseNode), "node");
					var p2 = Expression.Parameter(typeof(List< SerializableEdge >), "edges");
					var p3 = Expression.Parameter(typeof(NodePort), "port");

					MethodCallExpression ex;
					if (nodePortSignature)
						ex = Expression.Call(Expression.Convert(p1, type), method, p2, p3);
					else
						ex = Expression.Call(Expression.Convert(p1, type), method, p2);

					deleg = Expression.Lambda< CustomPortIODelegate >(ex, p1, p2, p3).Compile();
#endif

					if (deleg == null)
					{
						Debug.LogWarning("Can't use custom IO port function " + method + ": The method have to respect this format: " + typeof(CustomPortIODelegate));
						continue ;
					}
					
					var portInputAttr = (CustomPortInputAttribute)method.GetCustomAttribute(portInputType);
					var portOutputAttr = (CustomPortOutputAttribute)method.GetCustomAttribute(portOutputType);

					string fieldName = (portInputAttr == null) ? portOutputAttr.fieldName : portInputAttr.fieldName;
					Type customType = (portInputAttr == null) ? portOutputAttr.outputType : portInputAttr.inputType;
					Type fieldType = type.GetField(fieldName, bindingFlags).FieldType;

					AddCustomIOMethod(type, fieldName, deleg);

					AddAssignableTypes(customType, fieldType);
					AddAssignableTypes(fieldType, customType);
				}
			}
		}

		public static CustomPortIODelegate GetCustomPortMethod(Type nodeType, string fieldName)
		{
			customIOPortMethods.TryGetValue(nodeType, out PortIOPerField portIOPerField);

			if (portIOPerField == null)
				return null;

			portIOPerField.TryGetValue(fieldName, out CustomPortIODelegate deleg);

			return deleg;
		}

		static void AddCustomIOMethod(Type nodeType, string fieldName, CustomPortIODelegate deleg)
		{
			if (!customIOPortMethods.ContainsKey(nodeType))
				customIOPortMethods[nodeType] = new PortIOPerField();

			customIOPortMethods[nodeType][fieldName] = deleg;
		}

		static void AddAssignableTypes(Type fromType, Type toType)
		{
			if (!assignableTypes.ContainsKey(fromType))
				assignableTypes[fromType] = new List< Type >();

			assignableTypes[fromType].Add(toType);
		}

		public static bool IsAssignable(Type input, Type output)
		{
			if (assignableTypes.TryGetValue(input, out List<Type> type))
				return type.Contains(output);
			return false;
		}
	}
}
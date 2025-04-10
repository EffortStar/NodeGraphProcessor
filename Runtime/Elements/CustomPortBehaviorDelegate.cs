using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GraphProcessor
{
	public delegate IEnumerable<PortData> CustomPortBehaviorDelegate();

	public static class CustomPortBehaviour
	{
		private static readonly Dictionary<Type, List<(MethodInfo, string)>> s_customPortBehaviours = new();
		
		public static Dictionary<string, CustomPortBehaviorDelegate> Get(object instance)
		{
			Type type = instance.GetType();
			if (!s_customPortBehaviours.TryGetValue(type, out List<(MethodInfo, string)> customPortBehaviours))
			{
				s_customPortBehaviours.Add(type, customPortBehaviours = new List<(MethodInfo, string)>());
				Dictionary<string, NodeFieldInformation> nodeFields = NodeFieldInformation.GetInfoGroup(type);
				MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				foreach (MethodInfo method in methods)
				{
					var customPortBehaviorAttribute = method.GetCustomAttribute<CustomPortBehaviorAttribute>();
					if (customPortBehaviorAttribute == null)
						continue;

					string fieldName = customPortBehaviorAttribute.fieldName;
					if (nodeFields.ContainsKey(fieldName))
					{
						customPortBehaviours.Add((method, fieldName));
					}
					else
						Debug.LogError($"[NodeGraph] Invalid field name for custom port behavior: {method}, {fieldName}");
				}
			}
			
			Dictionary<string, CustomPortBehaviorDelegate> customPortBehaviour = new();
			foreach ((MethodInfo method, string fieldName) in customPortBehaviours)
			{
				// Check if custom port behavior function is valid
				CustomPortBehaviorDelegate behavior = null;
				try
				{
					Type referenceType = typeof(CustomPortBehaviorDelegate);
					behavior = (CustomPortBehaviorDelegate)Delegate.CreateDelegate(referenceType, instance, method, true);
				}
				catch
				{
					Debug.LogError("[NodeGraph] The function " + method + " cannot be converted to the required delegate format: " + typeof(CustomPortBehaviorDelegate));
				}
				customPortBehaviour.Add(fieldName, behavior);
			}
			
			return customPortBehaviour;
		}
	}
}
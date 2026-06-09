using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace GraphProcessor
{
	/// <summary>
	/// Runtime only information used for ports.
	/// </summary>
	public class NodeFieldInformation
	{
		public readonly string fieldName;
		public readonly FieldInfo info;
		public readonly bool input;
		public readonly bool isMultiple;
		public readonly bool isRequired;
		public readonly bool vertical;
#if UNITY_EDITOR
		public readonly EditorOnlyPortInfo EditorOnly;
#endif

		public NodeFieldInformation(
			FieldInfo info,
			bool input,
			bool isMultiple,
			bool vertical,
			bool isRequired
#if UNITY_EDITOR
			, EditorOnlyPortInfo editorOnly
#endif
		)
		{
			this.input = input;
			this.isMultiple = isMultiple;
			this.info = info;
			// Intern this string as it's referenced
			// across edges and ports many times.
			fieldName = string.Intern(info.Name);
			this.isRequired = isRequired;
			this.vertical = vertical;
#if UNITY_EDITOR
			EditorOnly = editorOnly;
#endif
		}

		private static readonly Dictionary<Type, Dictionary<string, NodeFieldInformation>> s_cache = new();

		public static bool TryGetInfo(Type type, string fieldName, [NotNullWhen(true)] out NodeFieldInformation info)
		{
			Dictionary<string, NodeFieldInformation> infoGroup = GetInfoGroup(type);
			return infoGroup.TryGetValue(fieldName, out info);
		}

		public static Dictionary<string, NodeFieldInformation> GetInfoGroup(Type type)
		{
			if (!s_cache.TryGetValue(type, out Dictionary<string, NodeFieldInformation> infoGroup))
			{
				s_cache.Add(type, infoGroup = CreateInfoGroup(type));
			}
			
			return infoGroup;
		}

		private static Dictionary<string, NodeFieldInformation> CreateInfoGroup(Type type)
		{
			Dictionary<string, NodeFieldInformation> infoGroup = new();
			do
			{
				foreach (FieldInfo field in type.GetFields(
					BindingFlags.Public 
					| BindingFlags.NonPublic 
					| BindingFlags.Instance
					| BindingFlags.DeclaredOnly
				))
				{
					ProcessField(field, infoGroup);
				}
				
				type = type.BaseType;
			} while (type != null && type != typeof(BaseNode));

			return infoGroup;

			static void ProcessField(FieldInfo field, Dictionary<string, NodeFieldInformation> infoGroup)
			{
				var hasInput = Attribute.IsDefined(field, typeof(InputAttribute));
				var hasOutput = Attribute.IsDefined(field, typeof(OutputAttribute));

				if (!hasInput && !hasOutput)
					return;
				
				var inputAttribute = field.GetCustomAttribute<InputAttribute>();
				var outputAttribute = field.GetCustomAttribute<OutputAttribute>();

				var isVertical = Attribute.IsDefined(field, typeof(VerticalAttribute));
				var isRequired = Attribute.IsDefined(field, typeof(RequiredPortAttribute));

				// check if field is a collection type
				bool isMultiple = inputAttribute?.allowMultiple ?? outputAttribute.allowMultiple;
				bool input = inputAttribute != null;

				// By default, we set the behavior to null, if the field have a custom behavior, it will be set in the loop just below
				infoGroup.Add(field.Name,
					new NodeFieldInformation(field,
						input,
						isMultiple,
						isVertical,
						isRequired
#if UNITY_EDITOR
						, new EditorOnlyPortInfo(field)
#endif
					)
				);
			}
		}
	}
}
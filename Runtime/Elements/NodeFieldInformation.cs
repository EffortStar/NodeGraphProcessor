using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using UnityEngine;
using UnityEngine.Pool;

namespace GraphProcessor
{
	public class NodeFieldInformation
	{
		public readonly string name;
		public readonly string fieldName;
		public readonly FieldInfo info;
		public readonly bool input;
		public readonly bool isMultiple;
		public readonly string tooltip;
		public readonly bool isRequired;
		public readonly bool vertical;

		public NodeFieldInformation(
			FieldInfo info,
			string name,
			bool input,
			bool isMultiple,
			string tooltip,
			bool vertical,
			bool isRequired
		)
		{
			this.input = input;
			this.isMultiple = isMultiple;
			this.info = info;
			this.name = name;
			// Intern this string as it's referenced
			// across edges and ports many times.
			fieldName = string.Intern(info.Name);
			this.isRequired = isRequired;
			this.tooltip = tooltip;
			this.vertical = vertical;
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
				var tooltipAttribute = field.GetCustomAttribute<TooltipAttribute>();


				// check if field is a collection type
				bool isMultiple = inputAttribute?.allowMultiple ?? outputAttribute.allowMultiple;
				bool input = inputAttribute != null;
				var tooltip = $"<b>{TypeUtility.FormatTypeName(field.FieldType)}</b>";
				if (tooltipAttribute != null)
				{
					tooltip += $"\n{tooltipAttribute.tooltip}";
				}

				string name = field.Name;
				if (inputAttribute is { name: not null })
					name = inputAttribute.name;
				if (outputAttribute is { name: not null })
					name = outputAttribute.name;

				// By default, we set the behavior to null, if the field have a custom behavior, it will be set in the loop just below
				infoGroup.Add(field.Name, new NodeFieldInformation(field, name, input, isMultiple, tooltip, isVertical, isRequired));
			}
		}
	}
}
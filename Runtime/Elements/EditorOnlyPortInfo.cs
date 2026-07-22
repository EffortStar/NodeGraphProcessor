#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GraphProcessor
{
	public readonly struct EditorOnlyPortInfo : IEquatable<EditorOnlyPortInfo>
	{
		[Flags]
		public enum FieldFlags
		{
			None = 0,
			Obsolete = 1 << 0
		}

		/// <summary>
		/// Display name on the node
		/// </summary>
		public readonly string DisplayName;
		public readonly string Tooltip;
		public readonly FieldFlags Flags;

		public EditorOnlyPortInfo(
			string displayName,
			string tooltip,
			FieldFlags flags
		)
		{
			Tooltip = tooltip;
			Flags = flags;
			DisplayName = displayName;
		}

		public EditorOnlyPortInfo(FieldInfo field)
		{
			if (Attribute.IsDefined(field, typeof(InputAttribute)) && field.GetCustomAttribute<InputAttribute>() is { name: { } inName })
			{
				DisplayName = inName;
			}
			else if (Attribute.IsDefined(field, typeof(OutputAttribute)) && field.GetCustomAttribute<OutputAttribute>() is { name: { } outName })
			{
				DisplayName = outName;
			}
			else
			{
				DisplayName = PascalToSentenceCase(field.Name);
			}

			Tooltip = Attribute.IsDefined(field, typeof(TooltipAttribute))
				? $"<b>{TypeUtility.FormatTypeName(field.FieldType)}</b>: {((TooltipAttribute)Attribute.GetCustomAttribute(field, typeof(TooltipAttribute))).tooltip}"
				: $"<b>{TypeUtility.FormatTypeName(field.FieldType)}</b>";

			Flags = FieldFlags.None;
			if (Attribute.IsDefined(field, typeof(ObsoleteAttribute)))
			{
				Flags |= FieldFlags.Obsolete;
			}

			return;

			static string PascalToSentenceCase(string str)
			{
				string result = Regex.Replace(str, "[a-z][A-Z]", m => $"{m.Value[0]} {m.Value[1]}");
				result = result.Replace(" Id", " ID");
				return result.Length > 2 ? $"{char.ToUpper(result[0])}{result[1..]}" : result;
			}
		}

		public bool Equals(EditorOnlyPortInfo other)
			=> DisplayName == other.DisplayName
				&& Tooltip == other.Tooltip
				&& Flags == other.Flags;

		public override bool Equals(object obj) => obj is EditorOnlyPortInfo other && Equals(other);

		public override int GetHashCode() => HashCode.Combine(Tooltip, (int)Flags, DisplayName);

		public static bool operator ==(EditorOnlyPortInfo left, EditorOnlyPortInfo right) => left.Equals(right);

		public static bool operator !=(EditorOnlyPortInfo left, EditorOnlyPortInfo right) => !left.Equals(right);
	}
}
#endif
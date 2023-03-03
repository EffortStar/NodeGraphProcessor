using System.Text;
using System;

namespace GraphProcessor
{
	public static class UssUtility
	{
		public static string PortVisualClass(Type type) => $"Port_{FormatTypeName(type)}";

		static string FormatTypeName(Type type) => TypeUtility.FormatTypeName(type, '-', '-', '_');
	}
}

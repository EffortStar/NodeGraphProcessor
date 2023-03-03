using System.Text;
using System;

namespace GraphProcessor
{
	public static class UssUtility
	{
		public static string PortVisualClass(Type type) // => $"Port_{FormatTypeName(type)}";
		{
			var result = $"Port_{FormatTypeName(type)}";
			UnityEngine.Debug.Log("Making port classs: " + result);
			return result;
		}


		static string FormatTypeName(Type type)
		{
			StringBuilder result = new StringBuilder();

			if (type.IsGenericType)
			{
				string[] parentType = type.Name.Split('`');
				// We will build the type here.
				Type[] arguments = type.GetGenericArguments();

				StringBuilder argList = new StringBuilder();
				foreach (Type t in arguments)
				{
					// Let's make sure we get the argument list.
					string arg = FormatTypeName(t);
					if (argList.Length > 0)
					{
						argList.AppendFormat("_{0}", arg);
					}
					else
					{
						argList.Append(arg);
					}
				}

				if (argList.Length > 0)
				{
					result.AppendFormat("{0}-{1}-", parentType[0], argList.ToString());
				}
			}
			else
			{
				return type.Name;
			}

			return result.ToString();
		}
	}
}

using System.Text;
using System;

namespace GraphProcessor
{
	public static class TypeUtility
	{
		public static string FormatTypeName(
			Type type,
			char leftBracket = '<',
			char rightBracket = '>',
			char dot = '.')
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
						argList.AppendFormat("{0}{1}", dot, arg);
					}
					else
					{
						argList.Append(arg);
					}
				}

				if (argList.Length > 0)
				{
					result.AppendFormat(
						"{0}{1}{2}{3}",
						parentType[0], leftBracket, argList, rightBracket
					);
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

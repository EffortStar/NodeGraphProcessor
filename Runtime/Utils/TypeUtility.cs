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
			char dot = '.',
			bool nicify = false)
		{
			if (type == typeof(int))
				return "Int";
			if (type == typeof(float))
				return "Float";
			if (type == typeof(bool))
				return "Bool";
			if (type == typeof(string))
				return "String";

			string resultString;
			if (!type.IsGenericType)
			{
				resultString = type.Name;
			}
			else
			{

				var result = new StringBuilder();
				string[] parentType = type.Name.Split('`');
				// We will build the type here.
				Type[] arguments = type.GetGenericArguments();

				var argList = new StringBuilder();
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

				resultString = result.ToString();
			}

#if UNITY_EDITOR
			return nicify ? UnityEditor.ObjectNames.NicifyVariableName(resultString) : resultString;
#else
			return resultString;
#endif
		}
	}
}
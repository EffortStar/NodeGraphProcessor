using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using JetBrains.Annotations;

namespace GraphProcessor
{
	public sealed class NodeInformation
	{
		private static readonly Dictionary<Type, NodeInformation> s_cache = new();

		private readonly NodeFieldPath[] _paths;

		public readonly IReadOnlyDictionary<string, NodeFieldInformation> Ports;

		private NodeInformation(List<NodeFieldPath> paths)
		{
			_paths = paths.ToArray();
			
			Dictionary<string, NodeFieldInformation> ports = new();
			GatherPorts(paths);
			Ports = ports;
			return;

			void GatherPorts(IList<NodeFieldPath> parent)
			{
				foreach (NodeFieldPath path in parent)
				{
					if (path.Children != null)
						
					{
						GatherPorts(path.Children);
					}
					else if (path.Info != null)
					{
						ports.Add(path.FieldPath, path.Info);
					}
				}
			}
		}


		public static bool TryGetInfo(Type type, string fieldPath, [NotNullWhen(true)] out NodeFieldInformation info)
		{
			NodeInformation infoGroup = GetInfoGroup(type);
			return infoGroup.TryGetInfo(fieldPath.AsSpan(), out info);
		}

		private bool TryGetInfo(ReadOnlySpan<char> path, out NodeFieldInformation info)
		{
			int indexOfSeparator = path.IndexOf(NodeFieldPath.Separator);
			if (indexOfSeparator < 0)
			{
				foreach (NodeFieldPath port in _paths)
				{
					if (!path.SequenceEqual(port.FieldPath))
					{
						continue;
					}

					info = port.Info;
					return true;
				}
			}
			else
			{
				ReadOnlySpan<char> query = path[..indexOfSeparator];
				ReadOnlySpan<char> remaining = path[(indexOfSeparator + 1)..];
				foreach (NodeFieldPath port in _paths)
				{
					if (!query.SequenceEqual(port.FieldPath))
					{
						continue;
					}

					return TryGetInfo(remaining, out info);
				}
			}

			info = null;
			return false;
		}

		public static NodeInformation GetInfoGroup(Type type)
		{
			if (!s_cache.TryGetValue(type, out NodeInformation infoGroup))
			{
				s_cache.Add(type, infoGroup = CreateInfoGroup(type));
			}
			
			return infoGroup;
		}

		private static NodeInformation CreateInfoGroup(Type type)
		{
			return new NodeInformation(ProcessFields(type, null));

			static List<NodeFieldPath> ProcessFields(Type type, [CanBeNull] NodeFieldPath parent)
			{
				List<NodeFieldPath> ports = new();
				do
				{
					foreach (FieldInfo field in type.GetFields(
						BindingFlags.Public 
						| BindingFlags.NonPublic 
						| BindingFlags.Instance
						| BindingFlags.DeclaredOnly
					))
					{
						if (Attribute.IsDefined(field, typeof(OutputObjectAttribute)))
						{
							// OutputObject
							NodeInformation info = GetInfoGroup(field.FieldType);
							ports.Add(new NodeFieldPath(GetPath(parent, field), field, parent, info._paths));
						}
						else if (Attribute.IsDefined(field, typeof(InputAttribute))
							|| Attribute.IsDefined(field, typeof(OutputAttribute)))
						{
							// Input/Output
							ports.Add(new NodeFieldPath(GetPath(parent, field), field, parent));
						}
					}
				
					type = type.BaseType;
				} while (type != null && type != typeof(BaseNode));

				return ports;

				string GetPath(NodeFieldPath parent, FieldInfo field) => parent == null ? field.Name : $"{parent.FieldPath}{NodeFieldPath.Separator}{field.Name}";
			}
		}
	}

	public sealed class NodeFieldPath
	{
		public const char Separator = '.';
		
		public readonly string FieldPath;
		public readonly FieldInfo FieldInfo;
		[CanBeNull] public readonly NodeFieldPath Parent;
		[CanBeNull] public readonly NodeFieldPath[] Children;
		[CanBeNull] public readonly NodeFieldInformation Info;
		
		public NodeFieldPath Root => Parent == null ? this : Parent.Root;

		public NodeFieldPath(
			string fieldPath,
			FieldInfo fieldInfo,
			[CanBeNull] NodeFieldPath parent,
			[CanBeNull] NodeFieldPath[] children
		)
		{
			FieldPath = fieldPath;
			FieldInfo = fieldInfo;
			Parent = parent;
			Children = children;
			Info = null;
		}
		
		public NodeFieldPath(
			string fieldPath,
			FieldInfo fieldInfo,
			[CanBeNull] NodeFieldPath parent
		)
		{
			FieldPath = fieldPath;
			FieldInfo = fieldInfo;
			Parent = parent;
			Children = null;
			
			var inputAttribute = fieldInfo.GetCustomAttribute<InputAttribute>();
			var outputAttribute = fieldInfo.GetCustomAttribute<OutputAttribute>();

			bool isVertical = Attribute.IsDefined(fieldInfo, typeof(VerticalAttribute));
			bool isRequired = Attribute.IsDefined(fieldInfo, typeof(RequiredPortAttribute));

			// check if field is a collection type
			bool allowMultiple = inputAttribute?.allowMultiple ?? outputAttribute.allowMultiple;

			Info = new NodeFieldInformation(
				this,
				inputAttribute != null,
				allowMultiple,
				isVertical,
				isRequired
#if UNITY_EDITOR
				,
				new EditorOnlyPortInfo(fieldInfo)
#endif
			);
		}
	}
	
	/// <summary>
	/// Runtime only information used for ports.
	/// </summary>
	public sealed class NodeFieldInformation
	{
		public readonly NodeFieldPath Path;
		public readonly bool IsInput;
		public readonly bool AllowMultiple;
		public readonly bool IsRequired;
		public readonly bool IsVertical;
#if UNITY_EDITOR
		public readonly EditorOnlyPortInfo EditorOnly;
#endif
		public Type FieldType => Path.FieldInfo.FieldType;

		public NodeFieldInformation(
			NodeFieldPath path,
			bool isInput,
			bool allowMultiple,
			bool isVertical,
			bool isRequired
#if UNITY_EDITOR
			, EditorOnlyPortInfo editorOnly
#endif
		)
		{
			Path = path;
			IsInput = isInput;
			AllowMultiple = allowMultiple;
			IsRequired = isRequired;
			IsVertical = isVertical;
#if UNITY_EDITOR
			EditorOnly = editorOnly;
#endif
		}

		public object GetValue(BaseNode owner)
		{
			Stack<NodeFieldPath> path = new();
			NodeFieldPath current = Path;
			do
			{
				path.Push(current);
				current = current.Parent;
			} while (current != null);

			// Starting at owner, get the value of all the fields down the path until we hit the last.
			object ctx = owner;
			while (ctx != null && path.TryPop(out current))
			{
				ctx = current.FieldInfo.GetValue(ctx);
			}

			return ctx;
		}

		public void SetValue(BaseNode owner, object value)
		{
			Stack<NodeFieldPath> path = new();
			NodeFieldPath current = Path;
			
			do
			{
				path.Push(current);
				current = current.Parent;
			} while (current != null);

			// Starting at owner, get the value of all the fields down the path until we hit the last.
			object ctx = owner;
			while (path.TryPop(out NodeFieldPath next) && next != Path)
			{
				current = next;
				ctx = current.FieldInfo.GetValue(ctx);
			}
			
			current?.FieldInfo.SetValue(ctx, value);
		}
	}
}
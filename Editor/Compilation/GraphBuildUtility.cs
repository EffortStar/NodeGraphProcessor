using System.IO;
using System.Linq;
using System.Reflection.Emit;
using UnityEditor;
using UnityEngine;

namespace GraphProcessor
{
	public static class GraphBuildUtility
	{
		/// <summary>
		/// Compiles edge push functions (<see cref="GraphExpressionCompilation.PushDataDelegate"/>) for all edges.<br/>
		/// Imports at <c>Assets/Plugins/Graph/Game.Graphs.Compiled.dll</c><br/>
		/// </summary>
		/// <remarks>The DLL isn't used in the editor, <c>Game.Graphs.Stub</c> is used instead.</remarks>
		[MenuItem("Effort Star/Graphs/Compile Edge Push Functions")]
		public static void CompileAllGraphsForBuild()
		{
			using GraphCompilation compilation = new(
				AssetDatabase.FindAssets("t:" + nameof(BaseGraph), new[] { "Assets" })
					.Select(guid => AssetDatabase.LoadAssetAtPath<BaseGraph>(AssetDatabase.GUIDToAssetPath(guid)))
					.Where(graph => graph != null)
#if DEBUG
				, debug: true
#endif
			);
			AssemblyBuilder builder = compilation.Compile();
			
			// Write file
			var assemblyFileName = $"{builder.GetName().Name}.dll";
			builder.Save(assemblyFileName);
			
			try
			{
				EditorApplication.LockReloadAssemblies();
				
				// Create the dll in the project
				const string pluginFolder = "Assets/Plugins/Graph";
				if (!Directory.Exists(pluginFolder))
					Directory.CreateDirectory(pluginFolder);
				string pluginPath = Path.Combine(pluginFolder, assemblyFileName);
				File.Delete(pluginPath);
				File.Move(assemblyFileName, pluginPath);
				
				// Import and ensure the plugin is set to be excluded from Editor.
				var pluginImporter = AssetImporter.GetAtPath(pluginPath) as PluginImporter;
				if (pluginImporter == null)
				{
					AssetDatabase.ImportAsset(pluginPath);
					pluginImporter = (PluginImporter)AssetImporter.GetAtPath(pluginPath);
				}
				EnsureBuildOnly(pluginImporter);
				
				Debug.Log($"Finished compiling to \"{Path.GetFullPath(pluginPath)}\".");
			}
			finally
			{
				EditorApplication.UnlockReloadAssemblies();
			}
			return;

			void EnsureBuildOnly(PluginImporter pluginImporter)
			{
				if (pluginImporter.GetCompatibleWithAnyPlatform() && pluginImporter.GetExcludeEditorFromAnyPlatform())
				{
					return;
				}
				pluginImporter.SetCompatibleWithAnyPlatform(true);
				pluginImporter.SetExcludeEditorFromAnyPlatform(true);
				pluginImporter.SaveAndReimport();
			}
		}
	}
}
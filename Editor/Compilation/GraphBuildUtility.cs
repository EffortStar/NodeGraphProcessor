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
			GraphCompilation compilation = new(
				AssetDatabase.FindAssets("t:" + nameof(BaseGraph), new[] { "Assets" })
					.Select(guid => AssetDatabase.LoadAssetAtPath<BaseGraph>(AssetDatabase.GUIDToAssetPath(guid)))
					.Where(graph => graph != null)
			);
			AssemblyBuilder builder = compilation.Compile();
			
			// Write file
			var assemblyFileName = $"{builder.GetName().Name}.dll";
			builder.Save(assemblyFileName);
			const string pluginFolder = "Assets/Plugins/Graph";
			if (!Directory.Exists(pluginFolder))
				Directory.CreateDirectory(pluginFolder);
			string pluginPath = Path.Combine(pluginFolder, assemblyFileName);
			File.Copy(assemblyFileName, pluginPath);
			try
			{
				EditorApplication.LockReloadAssemblies();
				AssetDatabase.ImportAsset(pluginPath);
				var pluginImporter = (PluginImporter)AssetImporter.GetAtPath(pluginPath);
				pluginImporter.SetCompatibleWithAnyPlatform(true);
				pluginImporter.SetExcludeEditorFromAnyPlatform(true);
				pluginImporter.SaveAndReimport();
				string libraryPath = Path.Combine("Library", "ScriptAssemblies", assemblyFileName);
				File.Delete(libraryPath);
				File.Move(assemblyFileName, libraryPath);
				Debug.Log($"Finished compiling to \"{Path.GetFullPath(pluginPath)}\".");
			}
			finally
			{
				EditorApplication.UnlockReloadAssemblies();
			}
		}
	}
}
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;

namespace GraphProcessor
{
	[Serializable]
	public abstract class BaseGraphWindow : EditorWindow
	{
		protected BaseGraphView graphView;
		private ToolbarToggle _subgraphIoToggle;

		[SerializeField]
		private BaseGraph _graph;

		[SerializeField]
		private List<BaseGraph> _graphBreadcrumbs;


		private const string GraphWindowStyle = "GraphProcessorStyles/BaseGraphView";
		private bool _reloadWorkaround;

		/// <summary>
		/// Called by Unity when the window is enabled / opened
		/// </summary>
		protected virtual void OnEnable()
		{
			rootVisualElement.name = "graphRootView";

			rootVisualElement.styleSheets.Add(Resources.Load<StyleSheet>(GraphWindowStyle));

			if (_graph != null)
				LoadGraph();
			else
				_reloadWorkaround = true;
		}

		protected virtual void Update()
		{
			// Workaround for the Refresh option of the editor window:
			// When Refresh is clicked, OnEnable is called before the serialized data in the
			// editor window is deserialized, causing the graph view to not be loaded
			if (_reloadWorkaround && _graph != null)
			{
				LoadGraph();
				_reloadWorkaround = false;
			}
		}

		void LoadGraph()
		{
			// We wait for the graph to be initialized
			if (_graph.isEnabled)
				InitializeGraph(_graph);
			else
				_graph.onEnabled += OnGraphEnabled;
		}

		private void OnGraphEnabled() => InitializeGraph(_graph);

		/// <summary>
		/// Called by Unity when the window is disabled (happens on domain reload)
		/// </summary>
		protected virtual void OnDisable()
		{
			if (_graph != null && graphView != null)
				graphView.SaveGraphToDisk();
		}

		/// <summary>
		/// Called by Unity when the window is closed
		/// </summary>
		protected virtual void OnDestroy()
		{
			if (_graph != null)
				_graph.onEnabled -= OnGraphEnabled;
		}

		public void InitializeGraph(BaseGraph graph)
		{
			if (this._graph != null && graph != this._graph)
			{
				// Save the graph to the disk
				EditorUtility.SetDirty(this._graph);
				AssetDatabase.SaveAssets();
			}

			_graph = graph;

			graphView?.RemoveFromHierarchy();

			if (graphView == null)
			{
				graphView = CreateView();
				Toolbar toolbar = new();
				rootVisualElement.Add(toolbar);
				AppendToToolbar(toolbar);
			}

			rootVisualElement.Insert(0, graphView);
			_reloadWorkaround = false;
			graphView.Initialize(graph);
			if (graph.IsLinkedToScene())
				LinkGraphWindowToScene(graph.GetLinkedScene());
			else
				graph.onSceneLinked += LinkGraphWindowToScene;

			GraphInitialized(graph);
		}

		protected abstract BaseGraphView CreateView();

		protected virtual void AppendToToolbar(Toolbar toolbar)
		{
			toolbar.Add(new ToolbarButton(() => graphView.ResetPositionAndZoom())
			{
				text = "Center",
				tooltip = "Frame the graph contents"
			});
			_subgraphIoToggle = new ToolbarToggle { text = "Subgraph IO" };
			_subgraphIoToggle.RegisterValueChangedCallback(
				_ => _subgraphIoToggle.SetValueWithoutNotify(graphView.ToggleView<SubgraphParameterView>())
			);
			toolbar.Add(_subgraphIoToggle);

			var toolbarSpacer = new ToolbarSpacer();
			toolbarSpacer.AddToClassList("toolbar__wide-spacer");
			toolbar.Add(toolbarSpacer);

			toolbar.Add(new ToolbarButton(() => EditorGUIUtility.PingObject(graphView.graph))
			{
				text = "Show In Project"
			});
		}

		protected virtual void GraphInitialized(BaseGraph graph)
		{
			_subgraphIoToggle.SetValueWithoutNotify((graphView.GetPinnedElementStatus<SubgraphParameterView>() & DropdownMenuAction.Status.Normal) != 0);
		}

		void LinkGraphWindowToScene(Scene scene)
		{
			EditorSceneManager.sceneClosed += CloseWindowWhenSceneIsClosed;
			return;

			void CloseWindowWhenSceneIsClosed(Scene closedScene)
			{
				if (scene == closedScene)
				{
					Close();
					EditorSceneManager.sceneClosed -= CloseWindowWhenSceneIsClosed;
				}
			}
		}

		public virtual void OnGraphDeleted()
		{
			if (_graph != null && graphView != null)
				rootVisualElement.Remove(graphView);

			graphView = null;
		}
	}
}
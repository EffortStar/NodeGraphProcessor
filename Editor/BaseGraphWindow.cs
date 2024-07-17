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
		private ToolbarBreadcrumbs _breadcrumbs;

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

		public void InitializeGraph(BaseGraph graph, bool isSubgraph = false)
		{
			if (_graph != null && graph != _graph)
			{
				// Save the graph to the disk
				EditorUtility.SetDirty(_graph);
				AssetDatabase.SaveAssets();
			}
			
			if (!isSubgraph)
				_graphBreadcrumbs?.Clear();

			_graph = graph;

			graphView?.RemoveFromHierarchy();

			TryAgain:
			if (graphView == null)
			{
				graphView = CreateView();
				var toolbar = rootVisualElement.Q<Toolbar>("graph-view-toolbar");
				if (toolbar == null)
				{
					toolbar = new Toolbar { name = "graph-view-toolbar" };
					rootVisualElement.Add(toolbar);
				}
				
				toolbar.Clear();
				AppendToToolbar(toolbar);
			}
			
			UpdateBreadcrumbs();

			try
			{
				
				rootVisualElement.Insert(0, graphView);
			}
			catch (Exception)
			{
				// I don't think this actually catches what is an error Unity prints and doesn't throw.
				// But if it does, this will fix it.
				graphView?.RemoveFromHierarchy();
				graphView = null;
				goto TryAgain;
			}

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

			_breadcrumbs = new ToolbarBreadcrumbs();
			toolbar.Add(_breadcrumbs);

			var toolbarSpacer = new ToolbarSpacer();
			toolbarSpacer.AddToClassList("toolbar__wide-spacer");
			toolbar.Add(toolbarSpacer);
		}

		private void UpdateBreadcrumbs()
		{
			_breadcrumbs.Clear();
			if (_graph == null)
				return;

			if (_graphBreadcrumbs != null)
			{
				foreach (BaseGraph graph in _graphBreadcrumbs)
				{
					if (graph == null) continue;
					_breadcrumbs.PushItem(ObjectNames.NicifyVariableName(graph.name), () => InitializeGraph(graph));
				}
			}

			_breadcrumbs.PushItem(ObjectNames.NicifyVariableName(_graph.name), () => EditorGUIUtility.PingObject(_graph));
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

		/// <summary>
		/// Opens a graph as a subgraph, appending the currently opened graph to the breadcrumbs.
		/// </summary>
		public void OpenSubgraph(BaseGraph subgraph)
		{
			_graphBreadcrumbs ??= new List<BaseGraph>();
			_graphBreadcrumbs.Add(_graph);
			InitializeGraph(subgraph, true);
		}
	}
}
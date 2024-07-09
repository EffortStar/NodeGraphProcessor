using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using System.Linq;
using System;

namespace GraphProcessor
{
	public sealed class SubgraphParameterView : PinnedElementView
	{
		private BaseGraphView graphView;

		private const string Title = "Subgraph IO";

		private readonly string exposedParameterViewStyle = "GraphProcessorStyles/SubgraphParameterView";

		private List<Rect> blackboardLayouts = new();

		public SubgraphParameterView()
		{
			var style = Resources.Load<StyleSheet>(exposedParameterViewStyle);
			if (style != null)
				styleSheets.Add(style);
			
			var userPortStyle = Resources.Load<StyleSheet>(PortView.UserPortStyleFile);
			if (userPortStyle != null)
				styleSheets.Add(userPortStyle);

			addItemRequested += OnAddClicked;
			// moveItemRequested += 
		}

		private void OnAddClicked(Blackboard blackboard)
		{
			var parameterType = new GenericMenu();

			foreach (Type paramType in GetExposedParameterTypes())
				parameterType.AddItem(new GUIContent(GetNiceNameFromType(paramType)), false, () =>
				{
					string uniqueName = "New " + GetNiceNameFromType(paramType);

					uniqueName = GetUniqueExposedPropertyName(uniqueName);
					graphView.graph.AddSubgraphParameter(uniqueName, paramType);
				});

			parameterType.ShowAsContext();
		}

		private string GetNiceNameFromType(Type type)
		{
			string name = type.Name;

			// Remove parameter in the name of the type if it exists
			name = name.Replace("Parameter", "");

			return ObjectNames.NicifyVariableName(name);
		}

		private string GetUniqueExposedPropertyName(string name)
		{
			// Generate unique name
			string uniqueName = name;
			int i = 0;
			while (graphView.graph.subgraphParameters.Any(e => e.Name == name))
				name = uniqueName + " " + i++;
			return name;
		}

		private IEnumerable<Type> GetExposedParameterTypes()
		{
			HashSet<Type> types = graphView.ports.Select(p => p.portType).ToHashSet();
			types.Remove(typeof(object));
			return types;
		}

		/*private void UpdateParameterList()
		{
			Content.Clear();
			Content.Add(new BlackboardSection
			{
				title = "Inputs",
			});

			foreach (SubgraphParameter param in graphView.graph.SubgraphParameters)
			{
				if (param.Direction != Direction.Input) continue;
				
				var row = new BlackboardRow(new SubgraphParameterFieldView(graphView, param), null) { expanded = false };
				Content.Add(row);
			}
			
			Content.Add(new BlackboardSection
			{
				title = "Outputs",
			});
			
			foreach (SubgraphParameter param in graphView.graph.SubgraphParameters)
			{
				if (param.Direction != Direction.Output) continue;
				
				var row = new BlackboardRow(new SubgraphParameterFieldView(graphView, param), null) { expanded = false };
				Content.Add(row);
			}
		}

		*/
		protected override void Initialize(BaseGraphView graphView)
		{
			this.graphView = graphView;
			title = Title;
			subTitle = null;
			scrollable = true;
			

			/*graphView.onSubgraphParameterListChanged += UpdateParameterList;
			graphView.initialized += UpdateParameterList;
			Undo.undoRedoPerformed += UpdateParameterList;

			RegisterCallback<DragUpdatedEvent>(OnDragUpdatedEvent);
			RegisterCallback<DragPerformEvent>(OnDragPerformEvent);
			RegisterCallback<MouseDownEvent>(OnMouseDownEvent, TrickleDown.TrickleDown);
			RegisterCallback<DetachFromPanelEvent>(OnViewClosed);

			UpdateParameterList();

			// Add exposed parameter button
			Header.Add(new Button(OnAddClicked)
			{
				text = "+"
			});*/
		}
		/*

		private void OnViewClosed(DetachFromPanelEvent evt)
			=> Undo.undoRedoPerformed -= UpdateParameterList;

		private void OnMouseDownEvent(MouseDownEvent evt)
		{
			blackboardLayouts = Content.Children().Select(c => c.layout).ToList();
		}

		private int GetInsertIndexFromMousePosition(Vector2 pos)
		{
			pos = Content.WorldToLocal(pos);
			// We only need to look for y axis;
			float mousePos = pos.y;

			if (mousePos < 0)
				return 0;

			int index = 0;
			foreach (Rect layout in blackboardLayouts)
			{
				if (mousePos > layout.yMin && mousePos < layout.yMax)
					return index + 1;
				index++;
			}

			return Content.childCount;
		}

		private void OnDragUpdatedEvent(DragUpdatedEvent evt)
		{
			DragAndDrop.visualMode = DragAndDropVisualMode.Move;
			int newIndex = GetInsertIndexFromMousePosition(evt.mousePosition);
			object graphSelectionDragData = DragAndDrop.GetGenericData("DragSelection");

			if (graphSelectionDragData == null)
				return;

			foreach (ISelectable obj in graphSelectionDragData as List<ISelectable>)
			{
				if (obj is SubgraphParameterFieldView view)
				{
					VisualElement blackBoardRow = view.parent.parent.parent.parent.parent.parent;
					int oldIndex = Content.Children().ToList().FindIndex(c => c == blackBoardRow);
					// Try to find the blackboard row
					Content.Remove(blackBoardRow);

					if (newIndex > oldIndex)
						newIndex--;

					Content.Insert(newIndex, blackBoardRow);
				}
			}
		}

		private void OnDragPerformEvent(DragPerformEvent evt)
		{
			bool updateList = false;

			int newIndex = GetInsertIndexFromMousePosition(evt.mousePosition);
			foreach (ISelectable obj in DragAndDrop.GetGenericData("DragSelection") as List<ISelectable>)
			{
				if (obj is SubgraphParameterFieldView view)
				{
					if (!updateList)
						graphView.RegisterCompleteObjectUndo("Moved parameters");

					int oldIndex = graphView.graph.subgraphParameters.FindIndex(e => e == view.Parameter);
					SubgraphParameter parameter = graphView.graph.subgraphParameters[oldIndex];
					graphView.graph.subgraphParameters.RemoveAt(oldIndex);

					// Patch new index after the remove operation:
					if (newIndex > oldIndex)
						newIndex--;

					graphView.graph.subgraphParameters.Insert(newIndex, parameter);

					updateList = true;
				}
			}

			if (updateList)
			{
				graphView.graph.NotifyExposedParameterListChanged();
				evt.StopImmediatePropagation();
				UpdateParameterList();
			}
		}*/
	}
}
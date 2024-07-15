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
		private new BaseGraphView graphView;

		private const string Title = "Subgraph IO";

		private readonly string exposedParameterViewStyle = "GraphProcessorStyles/SubgraphParameterView";

		private readonly BlackboardSection _inputsSection;
		private readonly BlackboardSection _outputsSection;

		public SubgraphParameterView()
		{
			var style = Resources.Load<StyleSheet>(exposedParameterViewStyle);
			if (style != null)
				styleSheets.Add(style);

			var userPortStyle = Resources.Load<StyleSheet>(PortView.UserPortStyleFile);
			if (userPortStyle != null)
				styleSheets.Add(userPortStyle);

			addItemRequested += OnAddClicked;
			moveItemRequested += MoveItemRequested;

			_inputsSection = new BlackboardSection { title = "Inputs", canAcceptDrop = _ => true, name = "InputsSection" };
			Add(_inputsSection);
			_outputsSection = new BlackboardSection { title = "Outputs", canAcceptDrop = _ => true, name = "OutputsSection" };
			Add(_outputsSection);
		}

		private void OnAddClicked(Blackboard blackboard)
		{
			var typeMenu = new GenericMenu();

			foreach ((Direction direction, Type paramType, string typeName)
			         in GetPortTypes()
				         .Select(d => (d.direction, d.portType, TypeUtility.FormatTypeName(d.portType)))
				         .OrderBy(d => d.Item3))
			{
				typeMenu.AddItem(
					new GUIContent(
						(direction == Direction.Input ? "Inputs/" : "Outputs/") + typeName
					),
					false,
					() =>
					{
						string uniqueName = "New " + typeName;

						uniqueName = GetUniqueExposedPropertyName(uniqueName);
						graphView.graph.AddSubgraphParameter(
							uniqueName,
							paramType,
							(ParameterDirection)direction
						);
					}
				);
			}

			typeMenu.ShowAsContext();
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

		private IEnumerable<(Direction direction, Type portType)> GetPortTypes() => graphView.ports.Select(p => (p.direction, p.portType)).ToHashSet();

		private void UpdateParameterList()
		{
			_inputsSection.Clear();
			_outputsSection.Clear();

			foreach (SubgraphParameter param in graphView.graph.SubgraphParameters)
			{
				if (param.Direction != ParameterDirection.Input) continue;

				var row = new BlackboardRow(new SubgraphParameterFieldView(graphView, param), null)
				{
					expanded = false,
					name = param.Name.Replace(' ', '_')
				};
				_inputsSection.Add(row);
			}

			foreach (SubgraphParameter param in graphView.graph.SubgraphParameters)
			{
				if (param.Direction != ParameterDirection.Output) continue;

				var row = new BlackboardRow(new SubgraphParameterFieldView(graphView, param), null)
				{
					expanded = false,
					name = param.Name.Replace(' ', '_')
				};
				_outputsSection.Add(row);
			}
		}

		protected override void Initialize(BaseGraphView graphView)
		{
			this.graphView = graphView;
			title = Title;
			subTitle = null;
			scrollable = true;


			graphView.onSubgraphParameterListChanged += UpdateParameterList;
			graphView.initialized += UpdateParameterList;
			Undo.undoRedoPerformed += UpdateParameterList;
			RegisterCallback<DetachFromPanelEvent>(OnViewClosed);

			UpdateParameterList();
		}


		private void OnViewClosed(DetachFromPanelEvent evt)
			=> Undo.undoRedoPerformed -= UpdateParameterList;

		private void MoveItemRequested(Blackboard blackboard, int insertIndex, VisualElement element)
		{
			if (element is not SubgraphParameterFieldView)
				return;

			var section = element.GetFirstAncestorOfType<BlackboardSection>();
			var row = element.GetFirstAncestorOfType<BlackboardRow>();

			if (insertIndex == section.contentContainer.childCount)
				row.BringToFront();
			else
				row.PlaceBehind(section[insertIndex]);
			
			graphView.RegisterCompleteObjectUndo("Moved parameters");

			List<SubgraphParameter> subgraphParameters = graphView.graph.subgraphParameters;
			subgraphParameters.Clear();

			_inputsSection.Query<SubgraphParameterFieldView>().ForEach(f => subgraphParameters.Add(f.Parameter));
			_outputsSection.Query<SubgraphParameterFieldView>().ForEach(f => subgraphParameters.Add(f.Parameter));
			graphView.graph.NotifyExposedParameterListChanged();
		}
	}
}
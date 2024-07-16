using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace GraphProcessor
{
	public sealed class SubgraphParameterFieldView : BlackboardField
	{
		private const string NotPresentErrorUssClassName = nameof(SubgraphParameterFieldView) + "__error-label";
		private const string HasErrorUssClassName = nameof(SubgraphParameterFieldView) + "--has-error";

		private readonly BaseGraphView _graphView;
		private static readonly CustomStyleProperty<Color> s_portColor = new("--port-color");

		private Label _errorLabel;

		public SubgraphParameter Parameter { get; }

		public SubgraphParameterFieldView(BaseGraphView graphView, SubgraphParameter param) : base(null, param.Name, param.ShortType)
		{
			_graphView = graphView;
			Parameter = param;
			this.AddManipulator(new ContextualMenuManipulator(BuildContextualMenu));
			VisualElement iconElement = this.Q("icon");
			iconElement.AddToClassList("Port_" + param.ShortType);
			iconElement.RegisterCallback<CustomStyleResolvedEvent>(evt =>
			{
				if (evt.customStyle.TryGetValue(s_portColor, out Color color))
					((VisualElement)evt.currentTarget).style.backgroundColor = color;
			});
			iconElement.visible = true;

			(this.Q("textField") as TextField).RegisterValueChangedCallback((e) =>
			{
				param.Name = e.newValue;
				text = e.newValue;
				graphView.graph.UpdateSubgraphParameterName(param, e.newValue);
			});

			schedule.Execute(() => AddIfNotPresentInGraph(graphView)).Every(500);
		}

		private void AddIfNotPresentInGraph(BaseGraphView graphView)
		{
			bool missingParameter = graphView.graph.nodes.OfType<ParameterNode>().All(n => n.parameterGUID != Parameter.Guid);
			EnableInClassList(HasErrorUssClassName, missingParameter);
			if (missingParameter)
			{
				if (_errorLabel == null)
				{
					_errorLabel = new Label("Parameter was not present in the graph.");
					_errorLabel.AddToClassList(NotPresentErrorUssClassName);
				}

				if (_errorLabel.panel == null)
					Add(_errorLabel);
			}
			else
			{
				_errorLabel?.RemoveFromHierarchy();
			}
		}

		private void BuildContextualMenu(ContextualMenuPopulateEvent evt)
		{
			evt.menu.AppendAction("Rename", _ => OpenTextEditor(), DropdownMenuAction.AlwaysEnabled);
			evt.menu.AppendAction("Delete", _ => _graphView.graph.RemoveSubgraphParameter(Parameter), DropdownMenuAction.AlwaysEnabled);

			evt.StopPropagation();
		}
	}
}
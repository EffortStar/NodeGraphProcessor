using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace GraphProcessor
{
	public sealed class SubgraphParameterFieldView : BlackboardField
	{
		private readonly BaseGraphView _graphView;
		private static readonly CustomStyleProperty<Color> s_portColor = new("--port-color");

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
		}

		private void BuildContextualMenu(ContextualMenuPopulateEvent evt)
		{
			evt.menu.AppendAction("Rename", _ => OpenTextEditor(), DropdownMenuAction.AlwaysEnabled);
			evt.menu.AppendAction("Delete", _ => _graphView.graph.RemoveSubgraphParameter(Parameter), DropdownMenuAction.AlwaysEnabled);

			evt.StopPropagation();
		}
	}
}
using UnityEngine.UIElements;
using JetBrains.Annotations;

namespace GraphProcessor
{
	[NodeCustomEditor(typeof(ParameterNode)), UsedImplicitly]
	public sealed class ParameterNodeView : BaseNodeView
	{
		private ParameterNode parameterNode;

		public override void Enable(bool fromInspector = false)
		{
			parameterNode = (ParameterNode)nodeTarget;

			EnumField accessorSelector = new(parameterNode.accessor);
			accessorSelector.SetValueWithoutNotify(parameterNode.accessor);
			accessorSelector.RegisterValueChangedCallback(evt =>
			{
				parameterNode.accessor = (ParameterAccessor)evt.newValue;
				UpdatePort();
				controlsContainer.MarkDirtyRepaint();
				ForceUpdatePorts();
			});
        
			UpdatePort();
			controlsContainer.Add(accessorSelector);
        
			//    Find and remove expand/collapse button
			titleContainer.Remove(titleContainer.Q("title-button-container"));
			//    Remove Port from the #content
			topContainer.parent.Remove(topContainer);
			//    Add Port to the #title
			titleContainer.Add(topContainer);

			parameterNode.onParameterChanged += UpdateView;
			UpdateView();
		}

		private void UpdateView()
		{
			title = parameterNode.parameter?.Name;
		}

		private void UpdatePort()
		{
			if(parameterNode.accessor == ParameterAccessor.Set)
			{
				titleContainer.AddToClassList("input");
			}
			else
			{
				titleContainer.RemoveFromClassList("input");
			}
		}
	}
}
using UnityEngine.UIElements;
using JetBrains.Annotations;
using UnityEditor.Experimental.GraphView;

namespace GraphProcessor
{
	[NodeCustomEditor(typeof(ParameterNode)), UsedImplicitly]
	public sealed class ParameterNodeView : BaseNodeView
	{
		private ParameterNode _parameterNode;

		public override void Enable(bool fromInspector = false)
		{
			_parameterNode = (ParameterNode)nodeTarget;
        
			UpdatePort();
			
			//    Find and remove expand/collapse button
			titleContainer.Remove(titleContainer.Q("title-button-container"));
			//    Remove Port from the #content
			topContainer.parent.Remove(topContainer);
			//    Add Port to the #title
			titleContainer.Add(topContainer);

			_parameterNode.onParameterChanged += UpdateView;
			UpdateView();
		}

		private void UpdateView()
		{
			title = _parameterNode.Parameter?.Name;
		}

		private void UpdatePort()
		{
			if(_parameterNode.Parameter.Direction == Direction.Output)
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
using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace GraphProcessor
{
	[NodeCustomEditor(typeof(SimplifiedRelayNode)), UsedImplicitly]
	public sealed class SimplifiedRelayNodeView : BaseNodeView
	{
		public override string layoutStyle => "GraphProcessorStyles/RelayNode";
		
		public override void Enable()
		{
			// Remove useless elements
			this.Q("title").RemoveFromHierarchy();
			this.Q("divider").RemoveFromHierarchy();
			AddToClassList("hideLabels");
		}

		public override void SetPosition(Rect newPos)
		{
			base.SetPosition(new Rect(newPos.position, new Vector2(200, 200)));
			style.height = 20;
			style.width = 50;
		}

		public override void OnRemoved()
		{
			// We delay the connection of the edges just in case something happens to the nodes we are trying to connect
			// i.e. multiple relay node deletion
			schedule.Execute(() =>
			{
				List<EdgeView> inputEdges = inputPortViews[0].GetEdges();
				List<EdgeView> outputEdges = outputPortViews[0].GetEdges();

				if (inputEdges.Count == 0 || outputEdges.Count == 0)
					return;

				EdgeView inputEdge = inputEdges.First();

				foreach (EdgeView outputEdge in outputEdges.ToList())
				{
					var input = outputEdge.input as PortView;
					var output = inputEdge.output as PortView;

					owner.Connect(output, input);
				}
			}).ExecuteLater(1);
		}
	}
}
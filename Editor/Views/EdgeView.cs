using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using UnityEngine;

namespace GraphProcessor
{
	public class EdgeView : Edge
	{
		public bool isConnected = false;

		public SerializableEdge serializedEdge => userData as SerializableEdge;

		readonly string edgeStyle = "GraphProcessorStyles/EdgeView";

		protected BaseGraphView owner => ((input ?? output) as PortView).owner.owner;

		public EdgeView()
		{
			styleSheets.Add(Resources.Load<StyleSheet>(edgeStyle));
			RegisterCallback<MouseDownEvent>(OnMouseDown);
		}

		public override void OnPortChanged(bool isInput)
		{
			base.OnPortChanged(isInput);
			UpdateEdgeSize();
		}

		public void UpdateEdgeSize()
		{
			if (input == null && output == null)
				return;

			PortData inputPortData = ((PortView)input)?.portData;
			PortData outputPortData = ((PortView)output)?.portData;

			for (var i = 1; i < 20; i++)
				RemoveFromClassList($"edge_{i}");
			int maxPortSize = Mathf.Max(inputPortData?.sizeInPixel ?? 0, outputPortData?.sizeInPixel ?? 0);
			if (maxPortSize > 0)
				AddToClassList($"edge_{Mathf.Max(1, maxPortSize - 6)}");
		}

		protected override void OnCustomStyleResolved(ICustomStyle styles)
		{
			base.OnCustomStyleResolved(styles);

			UpdateEdgeControl();
		}

		void OnMouseDown(MouseDownEvent e)
		{
			if (e.clickCount == 2)
			{
				// Empirical offset:
				Vector2 position = e.mousePosition;
				position += new Vector2(-10f, -28);
				Vector2 mousePos = owner.ChangeCoordinatesTo(owner.contentViewContainer, position);

				owner.AddRelayNode((PortView)input, (PortView)output, mousePos);
			}
		}
	}
}
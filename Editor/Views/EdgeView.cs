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

		protected BaseGraphView owner => ((input ?? output) as PortView).Owner.owner;

		public EdgeView()
		{
			styleSheets.Add(Resources.Load<StyleSheet>(edgeStyle));
			RegisterCallback<MouseDownEvent>(OnMouseDown);
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
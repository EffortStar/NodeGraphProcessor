using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using UnityEngine;

namespace GraphProcessor
{
	public class EdgeView : Edge
	{
		public bool IsConnected = false;

		public SerializableEdge SerializedEdge => userData as SerializableEdge;

		private const string EdgeStyle = "GraphProcessorStyles/EdgeView";

		private BaseGraphView Owner => ((input ?? output) as PortView).Owner.Owner;

		public EdgeView()
		{
			styleSheets.Add(Resources.Load<StyleSheet>(EdgeStyle));
			RegisterCallback<MouseDownEvent>(OnMouseDown);
		}

		protected override void OnCustomStyleResolved(ICustomStyle styles)
		{
			base.OnCustomStyleResolved(styles);

			UpdateEdgeControl();
		}

		private void OnMouseDown(MouseDownEvent e)
		{
			if (e.clickCount == 2)
			{
				// Empirical offset:
				Vector2 position = e.mousePosition;
				position += new Vector2(-10f, -28);
				Vector2 mousePos = Owner.ChangeCoordinatesTo(Owner.contentViewContainer, position);

				Owner.AddRelayNode((PortView)input, (PortView)output, mousePos);
			}
		}
	}
}
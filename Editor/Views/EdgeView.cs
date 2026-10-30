using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using UnityEngine;

namespace GraphProcessor
{
	public class EdgeView : Edge
	{
        private static readonly CustomStyleProperty<string> s_edgeStyleProperty = new("--edge-style");
		private static readonly CustomStyleProperty<string> s_curveStyleProperty = new("--curve-style");
		
		public bool IsConnected = false;
		private BetterEdgeControl _edgeControl;

		public SerializableEdge SerializedEdge => userData as SerializableEdge;

		private const string EdgeStyle = "GraphProcessorStyles/EdgeView";

		private BaseGraphView Owner => ((input ?? output) as PortView).Owner.Owner;

		public EdgeView()
		{
			styleSheets.Add(Resources.Load<StyleSheet>(EdgeStyle));
			RegisterCallback<MouseDownEvent>(OnMouseDown);
		}
		
		protected override EdgeControl CreateEdgeControl() =>
			_edgeControl = new BetterEdgeControl
			{
				capRadius = 4f,
				interceptWidth = 6f
			};

		protected override void OnCustomStyleResolved(ICustomStyle styles)
		{
			base.OnCustomStyleResolved(styles);
			
			// Collect the --edge-style property that defines how the edge is rendered.
			if (styles.TryGetValue(s_edgeStyleProperty, out string edgeState))
			{
				_edgeControl.EdgeStyle = edgeState switch
				{
					"dashed" => BetterEdgeControl.RenderStyle.Dashed,
					"hidden" => BetterEdgeControl.RenderStyle.HiddenWithPortArrows,
					"hidden-completely" => BetterEdgeControl.RenderStyle.HiddenCompletely,
					_ => BetterEdgeControl.RenderStyle.Visible
				};
			}
			else
			{
				_edgeControl.EdgeStyle = BetterEdgeControl.RenderStyle.Visible;
			}
			
			// --curve-style property that defines the edge's curve.
			if (styles.TryGetValue(s_curveStyleProperty, out string curveStyle))
			{
				_edgeControl.CurveStyle = curveStyle switch
				{
					"straight" => BetterEdgeControl.CurveType.Straight,
					_ => BetterEdgeControl.CurveType.Default
				};
			}
			else
			{
				_edgeControl.CurveStyle = BetterEdgeControl.CurveType.Default;
			}

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
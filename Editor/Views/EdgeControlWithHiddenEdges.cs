using System.Collections.Generic;
using System.Reflection;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

namespace GraphProcessor
{
	/// <inheritdoc />
	internal sealed class BetterEdgeControl : EdgeControl
	{
		private readonly List<Vector2> _renderPoints;
		private readonly Vector2[] _lastLocalControlPoints = new Vector2[4];

		public enum RenderStyle
		{
			/// <summary>
			/// A completely visible edge, rendered normally.
			/// </summary>
			Visible,
			/// <summary>
			/// No edge is rendered. Arrows are drawn between connected ports.
			/// </summary>
			HiddenWithPortArrows,
			/// <summary>
			/// No edge is rendered.
			/// </summary>
			HiddenCompletely,
			/// <summary>
			/// Renders a completely visible edge using a dashed line.
			/// </summary>
			Dashed
		}

		public enum CurveType
		{
			/// <summary>
			/// The default GraphView style.
			/// </summary>
			Default,
			/// <summary>
			/// A straight line.
			/// </summary>
			Straight
		}

		private RenderStyle _edgeStyle;
		private CurveType _lastCurveStyle;
		private CurveType _curveStyle;

		/// <summary>
		/// How the edge renders.
		/// </summary>
		public RenderStyle EdgeStyle
		{
			set
			{
				if (_edgeStyle == value)
				{
					return;
				}

				_edgeStyle = value;
				MarkDirtyRepaint();
			}
		}

		/// <summary>
		/// The type of curve used to draw the edge.
		/// </summary>
		public CurveType CurveStyle
		{
			set
			{
				if (_curveStyle == value)
				{
					return;
				}

				_curveStyle = value;
				PointsChanged();
			}
		}

		private bool _renderPointsDirty = true;
		private static readonly FieldInfo s_renderPointsDirtyField = typeof(EdgeControl).GetField("m_RenderPointsDirty", BindingFlags.NonPublic | BindingFlags.Instance);
		private static readonly FieldInfo s_lastLocalControlPointsField = typeof(EdgeControl).GetField("lastLocalControlPoints", BindingFlags.NonPublic | BindingFlags.Instance);

		public BetterEdgeControl()
		{
			// Override default edge drawing.
			generateVisualContent = GenerateVisualContent;
			_renderPoints = (List<Vector2>)typeof(EdgeControl)
				.GetField("m_RenderPoints", BindingFlags.NonPublic | BindingFlags.Instance)!
				.GetValue(this);
		}

		private void GenerateVisualContent(MeshGenerationContext mgc)
		{
			Profiler.BeginSample("DrawEdge");
			DrawEdge(mgc);
			Profiler.EndSample();
		}

		private static readonly Gradient Gradient = new();

		private static readonly FieldInfo GraphViewField =
			typeof(EdgeControl).GetField("m_GraphView", BindingFlags.NonPublic | BindingFlags.Instance);

		/// <inheritdoc />
		protected override void UpdateRenderPoints()
		{
			ComputeControlPoints(); // This should have been updated before : make sure anyway.
			bool curveStyleChanged = _lastCurveStyle != _curveStyle;
			_renderPointsDirty |= curveStyleChanged;

			if (!_renderPointsDirty)
				return;

			Vector2 p1 = parent.ChangeCoordinatesTo(this, controlPoints[0]);
			Vector2 p2 = parent.ChangeCoordinatesTo(this, controlPoints[1]);
			Vector2 p3 = parent.ChangeCoordinatesTo(this, controlPoints[2]);
			Vector2 p4 = parent.ChangeCoordinatesTo(this, controlPoints[3]);

			// Only compute this when the "local" points have actually changed
			if (!curveStyleChanged &&
				Approximately(p1, _lastLocalControlPoints[0]) &&
				Approximately(p2, _lastLocalControlPoints[1]) &&
				Approximately(p3, _lastLocalControlPoints[2]) &&
				Approximately(p4, _lastLocalControlPoints[3]))
			{
				_renderPointsDirty = false;
				return;
			}

			_lastCurveStyle = _curveStyle;
			_lastLocalControlPoints[0] = p1;
			_lastLocalControlPoints[1] = p2;
			_lastLocalControlPoints[2] = p3;
			_lastLocalControlPoints[3] = p4;

			switch (_curveStyle)
			{
				// Draw a straight line.
				case CurveType.Straight:
					_renderPoints.Clear();
					_renderPoints.Add(p1);
					_renderPoints.Add(p4);
					_renderPointsDirty = false;
					return;
				case CurveType.Default:
				default:
					if (curveStyleChanged)
					{
						// Cannot call PointsChanged because it calls MarkDirtyRepaint, which is not allowed at this time.
						// So we ensure the points are dirty ourselves, and clear the control points cache.
						s_renderPointsDirtyField.SetValue(this, true);
						((List<Vector2>)s_lastLocalControlPointsField.GetValue(this)).Clear();
					}

					// Fall back to default rendering.
					base.UpdateRenderPoints();
					break;
			}
		}

		private static bool Approximately(Vector2 v1, Vector2 v2)
			=> Mathf.Approximately(v1.x, v2.x) && Mathf.Approximately(v1.y, v2.y);

		/// <inheritdoc />
		protected override void ComputeControlPoints()
		{
			base.ComputeControlPoints();
			_renderPointsDirty = (bool)s_renderPointsDirtyField.GetValue(this);
		}

		/// <summary>
		///     Copied from EdgeControl.DrawEdge, with modifications to:<br />
		///     - Avoid drawing when marked as hidden.<br />
		///     - Render dashed lines.
		/// </summary>
		private void DrawEdge(MeshGenerationContext mgc)
		{
			if (edgeWidth <= 0)
			{
				return;
			}

			UpdateRenderPoints();
			if (_renderPoints.Count == 0)
			{
				return; // Don't draw anything
			}

			Color inColor = inputColor;
			Color outColor = outputColor;

			// TODO it would be nice to support this
			// inColor *= playModeTintColor;
			// outColor *= playModeTintColor;

			int cpt = _renderPoints.Count;
			Painter2D painter2D = mgc.painter2D;

			// Draw arrows at the port if the edge is hidden.
			if (_edgeStyle is RenderStyle.HiddenWithPortArrows)
			{
				const int divisions = 10;
				const float arrowAngle = Mathf.PI * 0.15f;
				const float portRadius = 8;
				const float arrowLength = portRadius + 5;

				// Arrows point between the connected ports.
				Vector2 dxy = (_renderPoints[1] - _renderPoints[0]).normalized;
				float a = Mathf.Atan2(dxy.y, dxy.x);

				// Draw using the opposing port's color.
				DrawArrow(_renderPoints[0], dxy, a, inColor);
				DrawArrow(_renderPoints[1], -dxy, a + Mathf.PI, outColor);
				return;

				void DrawArrow(Vector2 origin, Vector2 direction, float angle, Color color)
				{
					painter2D.fillColor = color;
					painter2D.BeginPath();
					// Head of the arrow.
					painter2D.MoveTo(origin + direction * arrowLength);
					// The circular base of the arrow.
					for (int i = 0; i <= divisions; i++)
					{
						float t = Mathf.Lerp(angle - arrowAngle, angle + arrowAngle, i / (float)divisions);
						painter2D.LineTo(origin + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * portRadius);
					}

					painter2D.Fill();
				}
			}

			float width = edgeWidth;
			float alpha = 1.0f;
			float zoom = ((GraphView)GraphViewField.GetValue(this))?.scale ?? 1.0f;

			if (edgeWidth * zoom < k_MinEdgeWidth)
			{
				alpha = edgeWidth * zoom / k_MinEdgeWidth;
				width = k_MinEdgeWidth / zoom;
			}

			Gradient.SetKeys(new[] { new GradientColorKey(outColor, 0), new GradientColorKey(inColor, 1) }, new[] { new GradientAlphaKey(alpha, 0) });
			painter2D.BeginPath();
			painter2D.strokeGradient = Gradient;

			painter2D.lineWidth = width;

			switch (_edgeStyle)
			{
				case RenderStyle.Dashed:
				{
					// TODO keep a running total of the distance to avoid restarting the dashes every segment.
					const float segmentsLength = 12f;
					Vector2 fromPos = _renderPoints[0];
					for (int i = 1; i < cpt; ++i)
					{
						Vector2 toPos = _renderPoints[i];
						float length = Vector2.Distance(fromPos, toPos);
						int count = Mathf.CeilToInt(length / segmentsLength);
						for (int t = 0; t < count; t += 2)
						{
							painter2D.MoveTo(Vector2.LerpUnclamped(fromPos, toPos, t * segmentsLength / length));
							painter2D.LineTo(Vector2.Lerp(fromPos, toPos, (t + 1) * segmentsLength / length));
						}

						fromPos = toPos;
					}

					break;
				}
				case RenderStyle.HiddenWithPortArrows:
				case RenderStyle.HiddenCompletely:
				{
					// Render nothing. The edge is hidden.
					return;
				}
				case RenderStyle.Visible:
				default:
				{
					painter2D.MoveTo(_renderPoints[0]);
					for (int i = 1; i < cpt; ++i)
					{
						painter2D.LineTo(_renderPoints[i]);
					}

					break;
				}
			}

			painter2D.Stroke();
		}

		/// <inheritdoc />
		public override bool ContainsPoint(Vector2 localPoint)
		{
			switch (_edgeStyle)
			{
				case RenderStyle.HiddenWithPortArrows:
				case RenderStyle.HiddenCompletely:
					// Edge is hidden, it's unselectable.
					return false;
				case RenderStyle.Visible:
				case RenderStyle.Dashed:
				default:
					// If the edge is visible, use default logic.
					return base.ContainsPoint(localPoint);
			}
		}

		/// <inheritdoc />
		public override bool Overlaps(Rect rect)
		{
			switch (_edgeStyle)
			{
				case RenderStyle.HiddenWithPortArrows:
				case RenderStyle.HiddenCompletely:
					// Edge is hidden, it's unselectable.
					return false;
				case RenderStyle.Visible:
				case RenderStyle.Dashed:
				default:
					// If the edge is visible, use default logic.
					return base.Overlaps(rect);
			}
		}
	}
}
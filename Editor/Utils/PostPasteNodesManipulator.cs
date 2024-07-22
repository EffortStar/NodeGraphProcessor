using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace GraphProcessor
{
	/// <summary>
	/// Allows groups of nodes to be placed under the cursor after pasting.
	/// </summary>
	internal sealed class PostPasteNodesManipulator : MouseManipulator
	{
		private readonly Dictionary<IPositionableView, Vector2> _elementOffsets;
		private readonly BaseGraphView _graphView;
		private readonly int _currentUndoGroup;

		// Taken from SelectionDragger
		private IVisualElementScheduledItem _panSchedule;
		internal const int k_PanAreaWidth = 100;
		internal const int k_PanSpeed = 4;
		internal const int k_PanInterval = 10;
		internal const float k_MaxSpeedFactor = 2.5f;
		internal const float k_MaxPanSpeed = k_MaxSpeedFactor * k_PanSpeed;
		private Vector3 m_PanDiff = Vector3.zero;
		private Vector3 m_ItemPanDiff = Vector3.zero;

		public PostPasteNodesManipulator(BaseGraphView graphView)
		{
			if (graphView.panel == null)
				return;

			_graphView = graphView;
			_elementOffsets = new Dictionary<IPositionableView, Vector2>();
			Vector2 min = graphView.selection
				.OfType<IPositionableView>()
				.Aggregate(new Vector2(float.PositiveInfinity, float.PositiveInfinity),
					(vector2, view) =>
					{
						Vector2 position = view.GetElementPosition();
						return new Vector2(Mathf.Min(vector2.x, position.x), Mathf.Min(vector2.y, position.y));
					});
			
			foreach (IPositionableView view in graphView.selection
				         .OfType<IPositionableView>())
			{
				_elementOffsets.Add(view, view.GetElementPosition() - min);
			}

			_currentUndoGroup = Undo.GetCurrentGroup();

			_panSchedule = graphView.schedule.Execute(Pan).Every(k_PanInterval).StartingIn(k_PanInterval);
			_panSchedule.Pause();
		}

		internal Vector2 GetEffectivePanSpeed(Vector2 mousePos)
		{
			Vector2 effectiveSpeed = Vector2.zero;

			if (mousePos.x <= k_PanAreaWidth)
				effectiveSpeed.x = -((k_PanAreaWidth - mousePos.x) / k_PanAreaWidth + 0.5f) * k_PanSpeed;
			else if (mousePos.x >= _graphView.contentContainer.layout.width - k_PanAreaWidth)
				effectiveSpeed.x = ((mousePos.x - (_graphView.contentContainer.layout.width - k_PanAreaWidth)) / k_PanAreaWidth + 0.5f) * k_PanSpeed;

			if (mousePos.y <= k_PanAreaWidth)
				effectiveSpeed.y = -((k_PanAreaWidth - mousePos.y) / k_PanAreaWidth + 0.5f) * k_PanSpeed;
			else if (mousePos.y >= _graphView.contentContainer.layout.height - k_PanAreaWidth)
				effectiveSpeed.y = ((mousePos.y - (_graphView.contentContainer.layout.height - k_PanAreaWidth)) / k_PanAreaWidth + 0.5f) * k_PanSpeed;

			effectiveSpeed = Vector2.ClampMagnitude(effectiveSpeed, k_MaxPanSpeed);

			return effectiveSpeed;
		}

		protected override void RegisterCallbacksOnTarget()
		{
			if (target.panel == null)
			{
				RemoveManipulator();
				return;
			}

			_graphView.RegisterCallback<MouseLeaveEvent>(RemoveManipulator);
			target.RegisterCallback<MouseUpEvent>(RemoveManipulator);
			target.RegisterCallback<DetachFromPanelEvent>(RemoveManipulator);
			target.RegisterCallback<MouseMoveEvent>(OnMouseMove);
			target.RegisterCallback<KeyDownEvent>(OnKeyDown);
			target.CaptureMouse();

			// --- I would love to set node positions here but mousePosition evaluates at (0, 0).
			// Even in the callback from capturing the mouse.
		}

		private void OnMouseMove(MouseMoveEvent evt)
		{
			Vector2 gvMousePos = target.ChangeCoordinatesTo(_graphView.contentContainer, evt.localMousePosition);

			m_PanDiff = GetEffectivePanSpeed(gvMousePos);
			if (m_PanDiff != Vector3.zero)
			{
				_panSchedule.Resume();
			}
			else
			{
				_panSchedule.Pause();
			}

			foreach ((IPositionableView key, Vector2 value) in _elementOffsets)
			{
				Rect position = key.GetPosition();
				position.position = evt.localMousePosition + value;
				key.SetPosition(position);
			}

			Undo.CollapseUndoOperations(_currentUndoGroup);
			evt.StopImmediatePropagation();
		}

		private void Pan(TimerState ts)
		{
			_graphView.viewTransform.position -= m_PanDiff;
			m_ItemPanDiff += m_PanDiff;
		}

		private void OnKeyDown(KeyDownEvent evt)
		{
			if (evt.keyCode == KeyCode.Return)
			{
				RemoveManipulator();
				evt.StopImmediatePropagation();
				return;
			}

			if (evt.keyCode is KeyCode.Escape or KeyCode.Delete or KeyCode.Backspace)
			{
				RemoveManipulator();
				Undo.PerformUndo();
				evt.StopImmediatePropagation();
				return;
			}
		}

		private void RemoveManipulator(MouseUpEvent evt)
		{
			RemoveManipulator();
			evt.StopImmediatePropagation();
		}

		private void RemoveManipulator(MouseLeaveEvent evt)
		{
			// RemoveManipulator();
		}

		private void RemoveManipulator(DetachFromPanelEvent evt) => RemoveManipulator();
		private void RemoveManipulator() => target.RemoveManipulator(this);

		protected override void UnregisterCallbacksFromTarget()
		{
			if (target.panel != null)
			{
				_panSchedule.Pause();

				if (m_ItemPanDiff != Vector3.zero)
				{
					Vector3 p = _graphView.contentViewContainer.transform.position;
					Vector3 s = _graphView.contentViewContainer.transform.scale;
					_graphView?.UpdateViewTransform(p, s);
				}
			}

			target.ReleaseMouse();
			_graphView?.UnregisterCallback<MouseLeaveEvent>(RemoveManipulator);
			target.UnregisterCallback<MouseUpEvent>(RemoveManipulator);
			target.UnregisterCallback<DetachFromPanelEvent>(RemoveManipulator);
			target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
			target.UnregisterCallback<KeyDownEvent>(OnKeyDown);
		}
	}
}
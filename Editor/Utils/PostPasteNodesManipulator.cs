using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GraphProcessor
{
	/// <summary>
	/// Allows groups of nodes to be placed under the cursor after pasting.
	/// </summary>
	internal sealed class PostPasteNodesManipulator : MouseManipulator
	{
		private readonly Dictionary<BaseNode, Vector2> _nodesOffsets;
		private readonly BaseGraphView _view;
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

		public PostPasteNodesManipulator(BaseGraphView view, Dictionary<string, BaseNode> copiedNodes)
		{
			if (view.panel == null)
				return;

			_view = view;
			_nodesOffsets = new Dictionary<BaseNode, Vector2>();
			Vector2 min = copiedNodes.Select(kvp => kvp.Value).Aggregate(new Vector2(float.PositiveInfinity, float.PositiveInfinity), (vector2, node) =>
				new Vector2(Mathf.Min(vector2.x, node.position.x), Mathf.Min(vector2.y, node.position.y))
			);
			foreach ((_, BaseNode value) in copiedNodes)
			{
				_nodesOffsets.Add(value, value.position - min);
			}

			_currentUndoGroup = Undo.GetCurrentGroup();

			_panSchedule = view.schedule.Execute(Pan).Every(k_PanInterval).StartingIn(k_PanInterval);
			_panSchedule.Pause();
		}
		
		internal Vector2 GetEffectivePanSpeed(Vector2 mousePos)
		{
			Vector2 effectiveSpeed = Vector2.zero;

			if (mousePos.x <= k_PanAreaWidth)
				effectiveSpeed.x = -((k_PanAreaWidth - mousePos.x) / k_PanAreaWidth + 0.5f) * k_PanSpeed;
			else if (mousePos.x >= _view.contentContainer.layout.width - k_PanAreaWidth)
				effectiveSpeed.x = ((mousePos.x - (_view.contentContainer.layout.width - k_PanAreaWidth)) / k_PanAreaWidth + 0.5f) * k_PanSpeed;

			if (mousePos.y <= k_PanAreaWidth)
				effectiveSpeed.y = -((k_PanAreaWidth - mousePos.y) / k_PanAreaWidth + 0.5f) * k_PanSpeed;
			else if (mousePos.y >= _view.contentContainer.layout.height - k_PanAreaWidth)
				effectiveSpeed.y = ((mousePos.y - (_view.contentContainer.layout.height - k_PanAreaWidth)) / k_PanAreaWidth + 0.5f) * k_PanSpeed;

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

			_view.RegisterCallback<MouseLeaveEvent>(RemoveManipulator);
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
			Vector2 gvMousePos = target.ChangeCoordinatesTo(_view.contentContainer, evt.localMousePosition);

			m_PanDiff = GetEffectivePanSpeed(gvMousePos);
			if (m_PanDiff != Vector3.zero)
			{
				_panSchedule.Resume();
			}
			else
			{
				_panSchedule.Pause();
			}
			
			foreach ((BaseNode key, Vector2 value) in _nodesOffsets)
			{
				_view.nodeViewsPerNode[key].SetPosition(evt.localMousePosition + value);
			}

			Undo.CollapseUndoOperations(_currentUndoGroup);
			evt.StopImmediatePropagation();
		}
		
		private void Pan(TimerState ts)
		{
			_view.viewTransform.position -= m_PanDiff;
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
					Vector3 p = _view.contentViewContainer.transform.position;
					Vector3 s = _view.contentViewContainer.transform.scale;
					_view.UpdateViewTransform(p, s);
				}
			}
			
			target.ReleaseMouse();
			_view.UnregisterCallback<MouseLeaveEvent>(RemoveManipulator);
			target.UnregisterCallback<MouseUpEvent>(RemoveManipulator);
			target.UnregisterCallback<DetachFromPanelEvent>(RemoveManipulator);
			target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
			target.UnregisterCallback<KeyDownEvent>(OnKeyDown);
		}
	}
}
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.Experimental.GraphView
{
	internal sealed class BetterResizer : VisualElement
	{
		private readonly Direction _direction;
		private Vector2 _start;
		private readonly Vector2 _minimumSize;
		private Rect _startRect;
		private readonly Action _onResizedCallback;
		private static readonly Vector2 s_resizerSize = new(30.0f, 30.0f);

		public MouseButton ActivateButton { get; set; }

		private bool _active;

		public enum Direction
		{
			Right,
			Bottom,
			Left,
			Top,
			BottomRight,
			BottomLeft,
			TopLeft,
			TopRight,
		}

		public BetterResizer(Direction direction) :
			this(s_resizerSize, direction)
		{
		}

		public BetterResizer(Direction direction, Action onResizedCallback) :
			this(s_resizerSize, direction, onResizedCallback)
		{
		}

		public BetterResizer(Vector2 minimumSize, Direction direction, Action onResizedCallback = null)
		{
			_minimumSize = minimumSize;
			_direction = direction;
			style.position = Position.Absolute;
			_active = false;
			_onResizedCallback = onResizedCallback;

			RegisterCallback<MouseDownEvent>(OnMouseDown);
			RegisterCallback<MouseUpEvent>(OnMouseUp);
			RegisterCallback<MouseMoveEvent>(OnMouseMove);

			ClearClassList();
			AddToClassList("resizer");

			AddToClassList(direction switch
			{
				Direction.Right => "resizer--right",
				Direction.Bottom => "resizer--bottom",
				Direction.Left => "resizer--left",
				Direction.Top => "resizer--top",
				Direction.BottomRight => "resizer--bottom-right",
				Direction.BottomLeft => "resizer--bottom-left",
				Direction.TopLeft => "resizer--top-left",
				Direction.TopRight => "resizer--top-right",
				_ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
			});
		}

		private void OnMouseDown(MouseDownEvent e)
		{
			if (_active)
			{
				e.StopImmediatePropagation();
				return;
			}

			IPanel panel = (e.target as VisualElement)?.panel;
			if (panel.GetCapturingElement(PointerId.mousePointerId) != null)
				return;

			if (parent is not GraphElement ce)
				return;

			if (!ce.IsResizable())
				return;

			if (e.button == (int)ActivateButton)
			{
				_start = this.ChangeCoordinatesTo(parent, e.localMousePosition);
				_startRect = ce.GetPosition();
				// Warn user if target uses a relative CSS position type
				if (parent.resolvedStyle.position == Position.Relative)
				{
					Debug.LogWarning("Attempting to resize an object with a non absolute position");
				}

				_active = true;
				this.CaptureMouse();
				e.StopPropagation();
			}
		}

		private void OnMouseUp(MouseUpEvent e)
		{
			if (parent is not GraphElement ce)
				return;

			if (!ce.IsResizable())
				return;

			if (!_active)
				return;

			if (e.button == (int)ActivateButton && _active)
			{
				_onResizedCallback?.Invoke();

				_active = false;
				this.ReleaseMouse();
				e.StopPropagation();
			}
		}

		private void OnMouseMove(MouseMoveEvent e)
		{
			if (parent is not GraphElement ce)
				return;

			if (!ce.IsResizable())
				return;

			// Then can be resize in all direction
			if (_direction is Direction.BottomRight or Direction.BottomLeft or Direction.TopRight or Direction.TopLeft)
			{
				if (ClassListContains("resizeAllDir") == false)
				{
					AddToClassList("resizeAllDir");
					RemoveFromClassList("resizeHorizontalDir");
					RemoveFromClassList("resizeVerticalDir");
				}
			}
			else if (_direction is Direction.Right or Direction.Left)
			{
				if (ClassListContains("resizeHorizontalDir") == false)
				{
					AddToClassList("resizeHorizontalDir");
					RemoveFromClassList("resizeAllDir");
					RemoveFromClassList("resizeVerticalDir");
				}
			}
			else if (_direction is Direction.Top or Direction.Bottom)
			{
				if (ClassListContains("resizeVerticalDir") == false)
				{
					AddToClassList("resizeVerticalDir");
					RemoveFromClassList("resizeAllDir");
					RemoveFromClassList("resizeHorizontalDir");
				}
			}

			if (_active)
			{
				Vector2 diff = this.ChangeCoordinatesTo(parent, e.localMousePosition) - _start;
				Rect size = ce.GetPosition();

				var resized = false;
				
				// 	var newSize = new Rect(_startRect.width + diff.x, _startRect.height + diff.y);

				Rect newSize;
				switch (_direction)
				{
					case Direction.Right:
						newSize = new Rect(size.x, size.y, _startRect.width + diff.x, size.height);
						break;
					case Direction.Bottom:
						newSize = new Rect(size.x, size.y, size.width, _startRect.height + diff.y);
						break;
					case Direction.Left:
						newSize = new Rect(_startRect.x + diff.x, size.y, _startRect.width - diff.x, size.height);
						break;
					case Direction.Top:
						newSize = new Rect(size.x, _startRect.y + diff.y, size.width, _startRect.height - diff.y);
						break;
					case Direction.BottomRight:
						newSize = new Rect(size.x, size.y, _startRect.width + diff.x, _startRect.height + diff.y);
						break;
					case Direction.BottomLeft:
						newSize = new Rect(_startRect.x + diff.x, size.y, _startRect.width - diff.x, _startRect.height + diff.y);
						break;
					case Direction.TopLeft:
						newSize = new Rect(_startRect.x + diff.x, _startRect.y + diff.y, _startRect.width - diff.x, _startRect.height - diff.y);
						break;
					case Direction.TopRight:
						newSize = new Rect(size.x, _startRect.y + diff.y, _startRect.width + diff.x, _startRect.height - diff.y);
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
				
				float minWidth = ce.resolvedStyle.minWidth == StyleKeyword.Auto ? 0 : ce.resolvedStyle.minWidth.value;
				minWidth = Math.Max(minWidth, _minimumSize.x);
				float minHeight = ce.resolvedStyle.minHeight == StyleKeyword.Auto ? 0 : ce.resolvedStyle.minHeight.value;
				minHeight = Math.Max(minHeight, _minimumSize.y);
				float maxWidth = ce.resolvedStyle.maxWidth == StyleKeyword.None ? float.MaxValue : ce.resolvedStyle.maxWidth.value;
				float maxHeight = ce.resolvedStyle.maxHeight == StyleKeyword.None ? float.MaxValue : ce.resolvedStyle.maxHeight.value;
				
				switch (_direction)
				{
					case Direction.Right:
					case Direction.BottomRight:
					case Direction.TopRight:
						newSize.width = (newSize.width < minWidth) ? minWidth : ((newSize.width > maxWidth) ? maxWidth : newSize.width);
						break;
					case Direction.Left:
					case Direction.BottomLeft:
					case Direction.TopLeft:
						newSize.x = (newSize.width < minWidth) ? newSize.xMax - minWidth : ((newSize.width > maxWidth) ? newSize.xMax - maxWidth : newSize.x);
						_start.x -= newSize.x - size.x;
						break;
				}
				
				switch (_direction)
				{
					case Direction.Bottom:
					case Direction.BottomRight:
					case Direction.BottomLeft:
						newSize.height = (newSize.height < minHeight) ? minHeight : ((newSize.height > maxHeight) ? maxHeight : newSize.height);
						break;
					case Direction.Top:
					case Direction.TopLeft:
					case Direction.TopRight:
						newSize.y = (newSize.height < minHeight) ? newSize.yMax - minHeight : ((newSize.height > maxHeight) ? newSize.yMax - maxHeight : newSize.y);
						_start.y -= newSize.y - size.y;
						break;
				}

				if (size != newSize)
				{
					ce.SetPosition(newSize);
					resized = true;
				}

				if (resized)
				{
					ce.UpdatePresenterPosition();

					var graphView = ce.GetFirstAncestorOfType<GraphView>();
					if (graphView is { elementResized: not null })
					{
						graphView.elementResized(ce);
					}
				}

				e.StopPropagation();
			}
		}
	}
}
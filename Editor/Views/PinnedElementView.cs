using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using UnityEngine;
using System;

namespace GraphProcessor
{
	public abstract class PinnedElementView : Blackboard
	{
		protected PinnedElement PinnedElement;

		protected event Action OnResized;

		private readonly Label _titleLabel;
		private bool _scrollable;
		private readonly ScrollView _scrollView;

		private static readonly string s_pinnedElementStyle = "GraphProcessorStyles/PinnedElementView";

		public PinnedElementView()
		{
			styleSheets.Add(Resources.Load<StyleSheet>(s_pinnedElementStyle));
			capabilities |= Capabilities.Movable | Capabilities.Resizable;
			style.overflow = Overflow.Hidden;

			ClearClassList();
			AddToClassList("pinnedElement");
			scrollable = false;
			VisualElement resizerIcon = this.Q<Resizer>()[0];
			resizerIcon.style.backgroundRepeat = StyleKeyword.Null;
			resizerIcon.style.backgroundSize = StyleKeyword.Null;
		}

		public void InitializeGraphView(PinnedElement pinnedElement, BaseGraphView graphView)
		{
			PinnedElement = pinnedElement;
			SetPosition(pinnedElement.position);
			Initialize(graphView);
		}

		public void ResetPosition()
		{
			PinnedElement.position = new Rect(Vector2.zero, PinnedElement.defaultSize);
			SetPosition(PinnedElement.position);
		}

		protected abstract void Initialize(BaseGraphView graphView);

		~PinnedElementView()
		{
			Destroy();
		}

		protected virtual void Destroy()
		{
		}
	}
}
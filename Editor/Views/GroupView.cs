using System;
using UnityEngine;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace GraphProcessor
{
	public class GroupView : GraphElement
	{
		public BaseGraphView Owner;
		public Group Group;

		private readonly Label _titleLabel;
		private readonly TextField _titleEditor;
		private readonly GroupDropArea _dropArea;
		private ColorField _colorField;
		private bool _editTitleCancelled = false;
		private readonly VisualElement _headerContainer;
		private readonly VisualElement _contentArea;
		private bool _initializing;

		private const string GroupStyle = "GraphProcessorStyles/GroupView";

		private static readonly Action<VisualElement, string> AddStyleSheetPath =
			(Action<VisualElement, string>)Delegate.CreateDelegate(typeof(Action<VisualElement, string>),
				typeof(VisualElement)
					.GetMethod("AddStyleSheetPath", BindingFlags.NonPublic | BindingFlags.Instance)!
			)!;

		public override bool IsResizable() => panel != null;

		public override string title
		{
			get => _titleLabel.text;
			set
			{
				if (_titleLabel.text == value)
					return;

				_titleLabel.text = value;
				Group.title = value;
				MarkDirtyRepaint();
			}
		}

		public GroupView()
		{
			VisualElement mainContainer = ((VisualTreeAsset)EditorGUIUtility.Load("UXML/GraphView/Scope.uxml")).Instantiate();
			mainContainer.AddToClassList("mainContainer");
			AddStyleSheetPath.Invoke(this, "StyleSheets/GraphView/Scope.uss");

			VisualElement titleContainer = ((VisualTreeAsset)EditorGUIUtility.Load("UXML/GraphView/GroupTitle.uxml")).Instantiate();
			AddStyleSheetPath.Invoke(this, "StyleSheets/GraphView/Group.uss");

			_dropArea = new GroupDropArea();
			_dropArea.ClearClassList();
			_dropArea.name = "dropArea";

			titleContainer.name = "titleContainer";
			_headerContainer = mainContainer.Q(name: "headerContainer");

			Add(mainContainer);

			_titleLabel = titleContainer.Q<Label>("titleLabel");

			_titleEditor = titleContainer.Q<TextField>("titleField");
			_titleEditor.style.display = DisplayStyle.None;

			var titleInput = _titleEditor.Q(TextField.textInputUssName);
			titleInput.RegisterCallback<FocusOutEvent>(e => OnEditTitleFinished(), TrickleDown.TrickleDown);
			titleInput.RegisterCallback<KeyDownEvent>(TitleEditorOnKeyDown, TrickleDown.TrickleDown);

			_contentArea = this.Q(name: "contentContainerPlaceholder");
			_contentArea.Insert(0, _dropArea);

			_headerContainer.Add(titleContainer);

			ClearClassList();
			AddToClassList("scope");
			AddToClassList("group");

			// Groups are not groupable and so do not have the groupable flag.
			capabilities |= Capabilities.Selectable | Capabilities.Movable | Capabilities.Deletable | Capabilities.Copiable;

			RegisterCallback<MouseDownEvent>(OnMouseDownEvent);

			styleSheets.Add(Resources.Load<StyleSheet>(GroupStyle));
			var resizer = new Resizer(new Vector2(200, 100), OnResizedCallback);
			hierarchy.Add(resizer);
			VisualElement resizerIcon = resizer[0];
			resizerIcon.pickingMode = PickingMode.Ignore;

			style.overflow = Overflow.Hidden;
			style.position = Position.Absolute;
		}

		private void TitleEditorOnKeyDown(KeyDownEvent e)
		{
			switch (e.keyCode)
			{
				case KeyCode.Escape:
					_editTitleCancelled = true;
					_titleEditor.Q(TextField.textInputUssName).Blur();
					break;
				case KeyCode.Return:
					_titleEditor.Q(TextField.textInputUssName).Blur();
					break;
			}
		}

		private void OnEditTitleFinished()
		{
			_titleLabel.visible = true;
			_titleEditor.style.display = DisplayStyle.None;

			if (!_editTitleCancelled)
			{
				title = _titleEditor.text;
			}

			_editTitleCancelled = false;
		}

		private void OnMouseDownEvent(MouseDownEvent e)
		{
			if (e.clickCount == 2)
			{
				if (HitTest(e.localMousePosition))
				{
					FocusTitleTextField();

					// Prevent MouseDown from refocusing the Label on PostDispatch
					e.StopImmediatePropagation();
				}
			}
		}

		public void FocusTitleTextField()
		{
			_titleEditor.SetValueWithoutNotify(title);
			_titleEditor.style.display = DisplayStyle.Flex;
			_titleLabel.visible = false;
			_titleEditor.textSelection.SelectAll();
			_titleEditor.Q(TextField.textInputUssName).Focus();
		}


		private void BuildContextualMenu(ContextualMenuPopulateEvent evt)
		{
			evt.menu.ClearItems();
			evt.menu.AppendAction("Delete Group", _ => Owner.RemoveElement(this));
		}

		public void Initialize(BaseGraphView graphView, Group block)
		{
			Group = block;
			Owner = graphView;

			title = block.title;
			SetPosition(block.position);

			this.AddManipulator(new ContextualMenuManipulator(BuildContextualMenu));

			_colorField = new ColorField { value = Group.color, name = "headerColorPicker" };
			_colorField.RegisterValueChangedCallback(e => UpdateGroupColor(e.newValue));
			UpdateGroupColor(Group.color);
			_headerContainer.Add(_colorField);
		}

		public void UpdateGroupColor(Color newColor)
		{
			Group.color = newColor;
			style.backgroundColor = newColor;
			float luminance = 0.2126f * Linearize(newColor.r) + 0.7152f * Linearize(newColor.g) + 0.0722f * Linearize(newColor.b);
			EnableInClassList("group--dark-title", LuminanceToLStar(luminance) * newColor.a > 40f);
			return;

			float Linearize(float colorChannel) => colorChannel <= 0.04045f ? colorChannel / 12.92f : Mathf.Pow((colorChannel + 0.055f) / 1.055f, 2.4f);

			float LuminanceToLStar(float y)
			{
				// Send this function a luminance value between 0.0 and 1.0,
				// and it returns L* which is "perceptual lightness"
				if (y <= 216 / 24389f)
					// The CIE standard states 0.008856 but 216/24389 is the intent for 0.008856451679036
					return y * (24389 / 27f); // The CIE standard states 903.3, but 24389/27 is the intent, making 903.296296296296296

				return Mathf.Pow(y, 1 / 3f) * 116 - 16;
			}
		}

		private void OnResizedCallback() => Group.position = GetPosition();

		public override void SetPosition(Rect newPos)
		{
			base.SetPosition(newPos);

			if (!_initializing)
				Owner.RegisterCompleteObjectUndo("Moved graph node");
			_initializing = false;

			Group.position = newPos;
		}

		public void EncapsulateElement(VisualElement element)
		{
			Rect rect = element.layout;
			float titleHeight = _titleEditor.parent.layout.height;
			titleHeight = float.IsNaN(titleHeight) ? 10 : titleHeight;

			if (Group.position == default)
			{
				Group.position.xMin = rect.xMin - 12;
				Group.position.yMin = rect.yMin - 12 - titleHeight;
				Group.position.xMax = rect.xMax + 12;
				Group.position.yMax = rect.yMax + 12;
			}
			else
			{
				Group.position.xMin = Mathf.Min(Group.position.x, rect.xMin - 12);
				Group.position.yMin = Mathf.Min(Group.position.y, rect.yMin - 12 - titleHeight);
				Group.position.xMax = Mathf.Max(Group.position.xMax, rect.xMax + 12);
				Group.position.yMax = Mathf.Max(Group.position.yMax, rect.yMax + 12);
			}

			SetPosition(Group.position);
		}

		internal class GroupDropArea : VisualElement, IDropTarget
		{
			private Rect _startRect;
			private Rect? _expandedOnceRect;
			private bool _validDragging;

			public bool CanAcceptDrop(List<ISelectable> selection)
			{
				if (selection.Count == 0)
					return false;

				return !selection.Cast<GraphElement>().Any(ge => ge is not Edge && (ge == null || ge is GroupView || !ge.IsGroupable()));
			}
			
			private bool CanAcceptDrop(IEnumerable<ISelectable> selection) => !selection.Cast<GraphElement>().Any(ge => ge is not Edge && (ge == null || ge is GroupView || !ge.IsGroupable()));

			public bool DragLeave(DragLeaveEvent evt, IEnumerable<ISelectable> selection, IDropTarget leftTarget, ISelection dragSource)
			{
				DragExited();
				return true;
			}

			public bool DragEnter(DragEnterEvent evt, IEnumerable<ISelectable> selection, IDropTarget enteredTarget, ISelection dragSource)
			{
				_validDragging = CanAcceptDrop(selection);
				_expandedOnceRect = null;
				_startRect = GetFirstAncestorOfType<GroupView>().GetPosition();
				return true;
			}

			public bool DragExited()
			{
				if (!_validDragging)
					return false;
				
				RemoveFromClassList("dragEntered");
				var group = GetFirstAncestorOfType<GroupView>();
				group._initializing = true; // avoid undo
				group.SetPosition(_startRect);
				return false;
			}

			public bool DragPerform(DragPerformEvent evt, IEnumerable<ISelectable> selection, IDropTarget dropTarget, ISelection dragSource)
			{
				if (!_validDragging)
					return false;
				
				var group = parent.GetFirstAncestorOfType<GroupView>();

				foreach (ISelectable selectable in selection)
				{
					if (selectable is Node element)
					{
						group._initializing = true;
						group.EncapsulateElement(element);
					}
				}

				group.SetPosition(group.GetPosition());

				RemoveFromClassList("dragEntered");
				return true;
			}

			public bool DragUpdated(DragUpdatedEvent evt, IEnumerable<ISelectable> selection, IDropTarget dropTarget, ISelection dragSource)
			{
				if (!_validDragging)
					return false;
				
				var group = parent.GetFirstAncestorOfType<GroupView>();
				var canDrop = false;

				foreach (ISelectable selectedElement in selection)
				{
					if (selectedElement == group || selectedElement is Edge)
						continue;

					var selectedGraphElement = selectedElement as GraphElement;
					bool dropCondition = selectedGraphElement != null
					                     && selectedGraphElement.IsGroupable();

					if (dropCondition)
					{
						canDrop = true;
					}
				}

				if (canDrop)
				{
					AddToClassList("dragEntered");

					group._initializing = true;
					group.SetPosition(_startRect);
					foreach (ISelectable selectedNode in selection)
					{
						if (selectedNode is not Node node) continue;
						group._initializing = true;
						group.EncapsulateElement(node);
					}

					Rect position = group.GetPosition();
					if (_expandedOnceRect == null)
					{
						_expandedOnceRect = position;
					}
					else
					{
						Rect value = _expandedOnceRect.Value;
						group._initializing = true;
						group.SetPosition(
							new Rect(
								Mathf.Max(value.x, position.x),
								Mathf.Max(value.y, position.y),
								Mathf.Min(value.width, position.width),
								Mathf.Min(value.height, position.height)
							)
						);
					}
				}
				else
				{
					RemoveFromClassList("dragEntered");
				}

				return true;
			}

			internal void OnStartDragging(IMouseEvent evt, IEnumerable<GraphElement> elements)
			{
				
			}
		}

		public void EnsureMinSize()
		{
			Rect position = GetPosition();
			if (position.width < 150 || position.height < 100)
			{
				position.width = Mathf.Max(150, position.width);
				position.height = Mathf.Max(100, position.height);
				SetPosition(position);
			}
		}
	}
}
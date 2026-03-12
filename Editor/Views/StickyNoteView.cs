#if UNITY_2020_1_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.Experimental.GraphView;
using UnityEditor.Search;
using UnityEngine.UIElements;

namespace GraphProcessor
{
	public sealed class StickyNoteView : UnityEditor.Experimental.GraphView.StickyNote
    {
        int m_ColorTheme;

        readonly DropdownField m_FontSizeDropdown;
        
        public BaseGraphView	owner;
        public StickyNote		note;
        private bool initializing;

        public StickyNoteView() : base("Packages/com.unity.visualeffectgraph/Editor/UIResources/uxml/VFXStickyNote.uxml", Vector2.zero)
        {
            this.styleSheets.Add(EditorGUIUtility.Load("StyleSheets/GraphView/Selectable.uss") as StyleSheet);
            this.styleSheets.Add(EditorGUIUtility.Load("StyleSheets/GraphView/StickyNote.uss") as StyleSheet);

            this.Q<Button>("swatch1").clicked += OnSwatch1;
            this.Q<Button>("swatch2").clicked += OnSwatch2;
            this.Q<Button>("swatch3").clicked += OnSwatch3;
            this.Q<Button>("fitToText").clicked += OnClickFitToText;

            m_FontSizeDropdown = this.Q<DropdownField>("fontSize");
            m_FontSizeDropdown.choices = new List<string> { "Small", "Medium", "Large", "Huge" };
            m_FontSizeDropdown.RegisterValueChangedCallback(OnFontSizeChanged);

            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.unity.visualeffectgraph/Editor/UIResources/uss/VFXStickyNote.uss"));
            this.capabilities |= Capabilities.Groupable;
            this.RegisterCallback<StickyNoteChangeEvent>(OnUIChange);
        }
        
        public void Initialize(BaseGraphView graphView, StickyNote note)
        {
	        initializing = true;
	        this.note = note;
	        owner = graphView;
	        title = note.title;
	        contents = note.content;
	        SetPosition(note.position);
	        SetTheme(note.theme);
	        fontSize = (StickyNoteFontSize)note.fontSize;
	        initializing = false;
        }

        void OnFontSizeChanged(ChangeEvent<string> evt)
        {
            if (Enum.TryParse<StickyNoteFontSize>(evt.newValue, out var newFontSize))
            {
                fontSize = newFontSize;
                UpdateFontSize();
            }
        }

        void OnClickFitToText()
        {
            FitText(false);
        }

        void OnSwatch1() => SetTheme(1);
        void OnSwatch2() => SetTheme(2);
        void OnSwatch3() => SetTheme(3);

        void SetTheme(int swatch)
        {
            if (swatch != m_ColorTheme)
            {
                // Remove inline color for background
                var nodeBorder = this.Q<VisualElement>("node-border");
                nodeBorder.style.backgroundColor = new StyleColor(StyleKeyword.Null);
                nodeBorder.style.borderTopColor = nodeBorder.style.borderRightColor = nodeBorder.style.borderBottomColor = nodeBorder.style.borderLeftColor = new StyleColor(StyleKeyword.Null);

                // Remove inline color for text
                this.Query<Label>().ForEach(x =>
                {
                    x.style.color = new StyleColor(StyleKeyword.Null);
                });

                RemoveFromClassList($"color-theme-{m_ColorTheme}");
                note.theme = swatch;
                m_ColorTheme = swatch;
                AddToClassList($"color-theme-{m_ColorTheme}");
            }
        }

        void OnUIChange(StickyNoteChangeEvent e)
        {
            switch (e.change)
            {
                case StickyNoteChange.Title:
	                if (!initializing)
		                owner.RegisterCompleteObjectUndo("Renamed sticky note");
                    note.title = title;
                    break;
                case StickyNoteChange.Contents:
	                if (!initializing)
		                owner.RegisterCompleteObjectUndo("Changed sticky note contents");
	                
	                note.content = contents;
                    break;
                case StickyNoteChange.FontSize:
	                if (!initializing)
		                owner.RegisterCompleteObjectUndo("Changed sticky note font size");
	                
                    m_FontSizeDropdown.SetValueWithoutNotify(fontSize.ToString());
                    UpdateFontSize();
                    break;
                case StickyNoteChange.Position:
	                if (!initializing)
		                owner.RegisterCompleteObjectUndo("Moved graph node");
	                
	                note.position = new Rect(resolvedStyle.left, resolvedStyle.top, style.width.value.value, style.height.value.value);
                    break;
            }
        }

        void UpdateFontSize()
        {
            var previousFontSizeString = note.fontSize;
            note.fontSize = (int)fontSize;
            if (fontSize > (StickyNoteFontSize)previousFontSizeString)
            {
                // Need to dispatch the fit text after the layout pass has properly taken the font size change
                Dispatcher.Enqueue(() => FitText(false), 0.1f);
            }
        }

        public override void OnResized()
        {
	        note.position = layout;
        }
    }
}
#endif
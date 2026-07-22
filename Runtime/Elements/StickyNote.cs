
using System;
using UnityEngine;

namespace GraphProcessor
{
    /// <summary>
    /// Serializable Sticky node class
    /// </summary>
    [Serializable]
    public class StickyNote
    {
        public Rect position;
        public string title;
        public string content = "Description";
        public int fontSize;
        public int theme = 1;

        public StickyNote(string title, Vector2 position)
        {
            this.title = title;
            this.position = new Rect(position.x, position.y, 200, 300);
        }
    }
}
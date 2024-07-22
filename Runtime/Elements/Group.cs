using System;
using UnityEngine;

namespace GraphProcessor
{
	/// <summary>
	/// Group the selected node when created
	/// </summary>
	[Serializable]
	public class Group
	{
		public string title;
		public Color color = new(0, 0, 0, 0.3f);
		public Rect position;

		// For serialization loading
		public Group()
		{
		}

		/// <summary>
		/// Create a new group with a title and a position
		/// </summary>
		/// <param name="title"></param>
		/// <param name="position"></param>
		public Group(string title, Vector2 position)
		{
			this.title = title;
			this.position.position = position;
		}
		
		public Group(string title)
		{
			this.title = title;
			position = default;
		}
	}
}
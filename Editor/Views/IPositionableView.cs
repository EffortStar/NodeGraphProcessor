using UnityEngine;

namespace GraphProcessor
{
	public interface IPositionableView
	{
		/// <summary>
		/// Gets the position of the view.
		/// </summary>
		Rect GetPosition();
		
		/// <summary>
		/// Sets the position of the view.
		/// </summary>
		void SetPosition(Rect position);
		
		/// <summary>
		/// Gets the position of the serialized data within the view.
		/// </summary>
		Vector2 GetElementPosition();
	}
}
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace GraphProcessor
{
	/// <summary>
	/// Helper class for managing an element's <see cref="IconBadge" />s.
	/// </summary>
	public sealed class IconBadges
	{
		private readonly VisualElement _root;
		private readonly VisualElement _attachmentTarget;
		private readonly List<IconBadge> _badges = new();

		public IconBadges(VisualElement root, VisualElement attachmentTarget)
		{
			_attachmentTarget = attachmentTarget;
			_root = root;
		}

		/// <summary>
		/// Adds a badge (an attached icon and message).
		/// </summary>
		public void AddBadge(
			string message,
			BadgeMessageType messageType,
			SpriteAlignment alignment = SpriteAlignment.TopRight,
			bool allowsRemoval = true
		)
		{
			IconBadge badge;
			switch (messageType)
			{
				case BadgeMessageType.Error:
					badge = IconBadge.CreateError(message);
					break;
				case BadgeMessageType.Info:
					badge = IconBadge.CreateComment(message);
					break;
				case BadgeMessageType.Warning:
					badge = new IconBadge
					{
						visualStyle = "warning",
						badgeText = message
					};
					break;
				default:
					goto case BadgeMessageType.Info;
			}

			// Force any children of the root to be un-pickable.
			// This makes it easy to detect if you're hovering an IconBadge, while not changing its behaviour.
			for (var i = 0; i < badge.childCount; i++)
			{
				badge[i].pickingMode = PickingMode.Ignore;
			}

			_root.Add(badge);
			if (allowsRemoval)
			{
				_badges.Add(badge);
			}

			badge.AttachTo(_attachmentTarget, alignment);
		}

		/// <summary>
		/// Removes a badge matching the provided <paramref name="message" />.
		/// </summary>
		public void RemoveBadge(string message) =>
			_badges.RemoveAll(b =>
			{
				if (b.badgeText != message)
				{
					return false;
				}

				b.Detach();
				b.RemoveFromHierarchy();
				return true;
			});

		/// <summary>
		/// Removes all badges.
		/// </summary>
		public void RemoveAllBadges()
		{
			foreach (IconBadge b in _badges)
			{
				b.Detach();
				b.RemoveFromHierarchy();
			}

			_badges.Clear();
		}

		/// <summary>
		/// Checks for the provided badge.
		/// </summary>
		public bool Contains(IconBadge badge) => _badges.Contains(badge);
	}
}
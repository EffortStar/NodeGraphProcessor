using System;

namespace GraphProcessor
{
	/// <summary>
	/// Class that describe port attributes for it's creation
	/// </summary>
	public class PortData : IEquatable<PortData>
	{
		/// <summary>
		/// Unique identifier for the port
		/// </summary>
		public string Path;
		
		/// <summary>
		/// Unique identifier for the port
		/// </summary>
		public string Identifier;

		/// <summary>
		/// The type that will be used for coloring with the type stylesheet
		/// </summary>
		public Type DisplayType;

		/// <summary>
		/// If the port accept multiple connection
		/// </summary>
		public bool AllowMultipleEdges;

		/// <summary>
		/// Is the port vertical
		/// </summary>
		public bool IsVertical;

		/// <summary>
		/// Does the port require an edge connection?
		/// </summary>
		public bool IsRequired;

		public bool IsInput;

#if UNITY_EDITOR
		public EditorOnlyPortInfo EditorOnly;
#endif

		public bool Equals(PortData other)
		{
			return other != null
				&& Identifier == other.Identifier
				&& DisplayType == other.DisplayType
				&& AllowMultipleEdges == other.AllowMultipleEdges
#if UNITY_EDITOR
				&& EditorOnly == other.EditorOnly
#endif
				&& IsVertical == other.IsVertical
				&& IsRequired == other.IsRequired;
		}
	}
}
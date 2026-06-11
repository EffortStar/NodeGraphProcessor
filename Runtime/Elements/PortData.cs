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
		public string Path { get; set; }
		
		/// <summary>
		/// Unique identifier for the port
		/// </summary>
		public string Identifier { get; set; }

		/// <summary>
		/// The type that will be used for coloring with the type stylesheet
		/// </summary>
		public virtual Type DisplayType { get; set; }

		/// <summary>
		/// If the port accept multiple connection
		/// </summary>
		public virtual bool AllowMultipleEdges { get; set; }

		/// <summary>
		/// Is the port vertical
		/// </summary>
		public bool IsVertical { get; set; }

		/// <summary>
		/// Does the port require an edge connection?
		/// </summary>
		public bool IsRequired { get; set; }

		public bool IsInput { get; set; }

#if UNITY_EDITOR
		public EditorOnlyPortInfo EditorOnly { get; set; }
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
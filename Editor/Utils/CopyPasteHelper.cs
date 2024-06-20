using System.Collections.Generic;

namespace GraphProcessor
{
	[System.Serializable]
	public class CopyPasteHelper
	{
		public List<JsonElement> copiedNodes = new();

		public List<JsonElement> copiedGroups = new();

		public List<JsonElement> copiedEdges = new();
	}
}
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace GraphProcessor
{
	public class GraphInspector : Editor
	{
		public sealed override VisualElement CreateInspectorGUI()
		{
			var root = new VisualElement();
			VisualElement inspector = new()
			{
				style = { marginBottom = 8 }
			};
			InspectorElement.FillDefaultInspector(inspector, serializedObject, this);
			root.Add(inspector);
			
			CreateInspector(root);
			
			return root;
		}

		protected virtual void CreateInspector(VisualElement root)
		{
			
		}

		// Don't use ImGUI
		public sealed override void OnInspectorGUI()
		{
		}
	}
}
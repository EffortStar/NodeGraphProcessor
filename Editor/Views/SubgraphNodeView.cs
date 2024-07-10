#nullable enable

using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace GraphProcessor
{
	[NodeCustomEditor(typeof(SubgraphNodeBase)), UsedImplicitly]
	public sealed class SubgraphNodeView : BaseNodeView
	{
		public new const string UssClassName = "subgraph-node";
		public const string TitleUssClassName = UssClassName + "__icon";
		
		private SubgraphNodeBase SubgraphNode => (SubgraphNodeBase)nodeTarget;

		protected override bool hasSettings => true;

		public SubgraphNodeView()
		{
			// Double-click to open subgraph
			RegisterCallback<MouseDownEvent>(evt =>
			{
				if (evt.button != 0 || evt.clickCount != 2)
					return;
				BaseGraph? subgraph = ((SubgraphNodeBase)nodeTarget).Subgraph;
				if (subgraph == null)
					return;
				AssetDatabase.OpenAsset(subgraph);
				evt.StopPropagation();
			});

			SetTitleIcon(TitleUssClassName);
		}

		protected override void InitializeView()
		{
			base.InitializeView();
			UpdateError();
		}

		protected override VisualElement CreateSettingsView()
		{
			VisualElement view = new();
			view.Add(base.CreateSettingsView());
			var subgraphField = new PropertyField(FindSerializedProperty("_subgraph"), "");
			subgraphField.RegisterValueChangeCallback(_ =>
			{
				ForceUpdatePorts();
				UpdateTitle();
				UpdateError();
			});
			view.Add(subgraphField);
			return view;
		}

		private void UpdateError()
		{
			const string noSubgraphAssignedError = "No Subgraph assigned";
			if (SubgraphNode.Subgraph == null)
			{
				AddBadge(noSubgraphAssignedError, BadgeMessageType.Error);
			}
			else
			{
				RemoveBadge(noSubgraphAssignedError);
			}
		}
	}
}
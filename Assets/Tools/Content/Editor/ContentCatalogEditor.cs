using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Tools.Content.Editor
{
    [CustomEditor(typeof(ContentCatalog))]
    public sealed class ContentCatalogEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var summary = new Label();
            var errors = new HelpBox("", HelpBoxMessageType.Error);
            root.Add(summary);
            root.Add(new Button(() => ContentBrowserWindow.Open((ContentCatalog)target)) { text = "Open Content Browser" });
            root.Add(errors);
            // Keep the asset Inspector compact; authoring and relationship navigation live in the browser.
            void Refresh()
            {
                if (target == null) return;
                var catalog = (ContentCatalog)target;
                summary.text = (catalog.entries?.Count ?? 0) + " Entries";
                var messages = catalog.Validate();
                errors.text = string.Join("\n", messages.Take(5));
                errors.style.display = messages.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            }
            Refresh();
            root.TrackSerializedObjectValue(serializedObject, _ => Refresh());
            return root;
        }
    }
}

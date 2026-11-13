using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tools.Georeferencing.Editor
{
    public sealed class RegionLibraryWindow : EditorWindow
    {
        const string LibraryFolder = "Tools/Georeferencing/RegionLibrary";
        [SerializeField] RegionCatalog catalog;
        RegionLibraryIndex library;
        RegionLibraryEntry selected;
        ListView list;
        TextField search;
        Toggle includeChildren;
        Label details, status;
        Button add;
        Action<ContentRegion> imported;

        public static void Open(RegionCatalog target, Action<ContentRegion> onImported)
        {
            var window = GetWindow<RegionLibraryWindow>("Region Library");
            window.catalog = target;
            window.imported = onImported;
            window.CreateGUI();
            window.Show();
        }

        public void CreateGUI()
        {
            minSize = new Vector2(650, 600);
            rootVisualElement.Clear();
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Tools/Georeferencing/Editor/Georeferencing.uss");
            if (!rootVisualElement.styleSheets.Contains(sheet)) rootVisualElement.styleSheets.Add(sheet);
            var content = new VisualElement();
            content.AddToClassList("tool-root");
            rootVisualElement.Add(content);
            var target = new ObjectField("Target Catalogue") { objectType = typeof(RegionCatalog), allowSceneObjects = false, value = catalog };
            target.RegisterValueChangedCallback(evt => { catalog = evt.newValue as RegionCatalog; UpdateSelection(); });
            content.Add(target);
            search = new TextField("Search");
            search.RegisterValueChangedCallback(_ => Filter());
            content.Add(search);
            var actions = new VisualElement();
            actions.AddToClassList("toolbar");
            actions.Add(new Button(LoadBundled) { text = "Bundled Regions" });
            actions.Add(new Button(LoadFile) { text = "Open Library File" });
            content.Add(actions);
            list = new ListView { fixedItemHeight = 24, selectionType = SelectionType.Single };
            list.AddToClassList("library-list");
            list.makeItem = () => new Label();
            list.bindItem = (element, index) =>
            {
                var entry = (RegionLibraryEntry)list.itemsSource[index];
                ((Label)element).text = library.PathLabel(entry);
                element.tooltip = library.PathLabel(entry);
            };
            list.selectionChanged += items => { selected = items.OfType<RegionLibraryEntry>().FirstOrDefault(); UpdateSelection(); };
            content.Add(list);
            details = new Label();
            details.AddToClassList("hint");
            content.Add(details);
            includeChildren = new Toggle("Include Subregions");
            includeChildren.RegisterValueChangedCallback(_ => UpdateSelection());
            content.Add(includeChildren);
            add = new Button(AddSelection) { text = "Add To Catalogue" };
            add.SetEnabled(false);
            content.Add(add);
            status = new Label("Select A Region; Missing Parents Are Included Automatically");
            status.AddToClassList("hint");
            content.Add(status);
            LoadBundled();
        }

        static IEnumerable<RegionLibraryEntry> Read(string path)
        {
            // Compressed GeoJSON keeps the source library compact and outside player builds.
            using var file = File.OpenRead(path);
            using Stream stream = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                ? new GZipStream(file, CompressionMode.Decompress) : file;
            using var reader = new StreamReader(stream);
            var document = JObject.Parse(reader.ReadToEnd());
            if ((string)document["type"] != "FeatureCollection") throw new FormatException("Open A Library FeatureCollection");
            var entries = new List<RegionLibraryEntry>();
            foreach (var feature in document["features"])
            {
                var properties = feature["properties"];
                if (!Enum.TryParse((string)properties?["level"], out RegionLevel level))
                    throw new FormatException("Each Library Feature Needs A Valid Level");
                entries.Add(new RegionLibraryEntry {
                    Id = (string)properties["regionId"], ParentId = (string)properties["parentId"],
                    Name = (string)properties["displayName"], Level = level,
                    Description = (string)properties["description"], DataSource = (string)properties["dataSource"],
                    GeometryJson = feature["geometry"]?.Type == JTokenType.Null ? null : feature["geometry"]?.ToString()
                });
            }
            return entries;
        }

        void LoadBundled()
        {
            try
            {
                var entries = new List<RegionLibraryEntry>();
                foreach (string path in Directory.GetFiles(LibraryFolder).Where(path => path.EndsWith(".geojson") || path.EndsWith(".geojson.gz")))
                    entries.AddRange(Read(path));
                Load(entries);
            }
            catch (Exception error) { status.text = error.Message; }
        }

        void LoadFile()
        {
            string path = EditorUtility.OpenFilePanel("Open Region Library", LibraryFolder, "geojson,gz,json");
            if (string.IsNullOrEmpty(path)) return;
            try { Load(Read(path).ToList()); }
            catch (Exception error) { status.text = error.Message; }
        }

        void Load(List<RegionLibraryEntry> entries)
        {
            library = new RegionLibraryIndex(entries);
            selected = null;
            Filter();
            status.text = entries.Count + " Regions Available; Source Assets Are Created Only When Added";
        }

        void Filter()
        {
            selected = null;
            list.ClearSelection();
            list.itemsSource = library.Entries.Where(entry => library.PathLabel(entry)
                .IndexOf(search.value ?? "", StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(library.PathLabel).ToList();
            list.Rebuild();
            UpdateSelection();
        }

        void UpdateSelection()
        {
            if (add == null) return;
            add.SetEnabled(catalog != null && selected != null);
            includeChildren.SetEnabled(selected != null);
            if (selected == null) { details.text = "Select A Region From The Library"; return; }
            int count = catalog == null ? 0 : library.Plan(selected, includeChildren.value,
                new HashSet<string>(catalog.regions.Where(region => region != null).Select(region => region.regionId))).Count;
            details.text = selected.Name + ": " + ObjectNames.NicifyVariableName(selected.Level.ToString())
                + "\n" + selected.Description + "\n" + selected.DataSource
                + (catalog == null ? "\nAssign A Target Catalogue" : "\n" + count + " New Regions; Existing Regions Are Kept");
        }

        void AddSelection()
        {
            if (catalog == null || selected == null) return;
            try
            {
                var errors = new List<string>();
                RegionResolver.Validate(catalog, errors);
                if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
                var existing = catalog.regions.ToDictionary(region => region.regionId, StringComparer.Ordinal);
                var plan = library.Plan(selected, includeChildren.value, new HashSet<string>(existing.Keys));
                if (plan.Count == 0)
                {
                    status.text = "All Requested Regions Already Exist; Existing Data Was Kept";
                    imported?.Invoke(existing[selected.Id]);
                    return;
                }
                // Parse every requested boundary before creating any assets.
                var boundaries = plan.ToDictionary(entry => entry.Id, entry => string.IsNullOrEmpty(entry.GeometryJson)
                    ? null : GeoJsonImporter.Parse(entry.GeometryJson));
                string folder = GeoreferencingWindow.RegionsFolder(catalog);
                Undo.RecordObject(catalog, "Add Library Regions");
                AssetDatabase.StartAssetEditing();
                try
                {
                    for (int index = 0; index < plan.Count; index++)
                    {
                        var entry = plan[index];
                        EditorUtility.DisplayProgressBar("Adding Library Regions", entry.Name, (float)index / plan.Count);
                        var region = CreateInstance<ContentRegion>();
                        region.regionId = entry.Id;
                        region.libraryRegionId = entry.Id;
                        region.displayName = entry.Name;
                        region.level = entry.Level;
                        region.parent = string.IsNullOrEmpty(entry.ParentId) ? null : existing[entry.ParentId];
                        region.description = entry.Description;
                        region.dataSource = entry.DataSource;
                        region.regionColor = RegionPalette.Next(catalog);
                        if (boundaries[entry.Id] != null)
                        {
                            region.boundary = RegionBoundary.Polygon;
                            region.polygons = boundaries[entry.Id];
                        }
                        // Stable IDs deduplicate imports; filenames remain local to the catalogue.
                        string filename = string.Concat((entry.Name ?? "Region").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                        AssetDatabase.CreateAsset(region, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + filename + ".asset"));
                        Undo.RegisterCreatedObjectUndo(region, "Add Library Region");
                        existing.Add(entry.Id, region);
                        catalog.regions.Add(region);
                    }
                    EditorUtility.SetDirty(catalog);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                    EditorUtility.ClearProgressBar();
                }
                UpdateSelection();
                status.text = "Added " + plan.Count + " Regions; Save Assets In The Georeferencing Window";
                imported?.Invoke(existing[selected.Id]);
            }
            catch (Exception error) { status.text = error.Message; }
        }
    }
}

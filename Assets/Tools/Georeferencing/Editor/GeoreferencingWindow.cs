using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tools.Georeferencing.Editor
{
    public sealed class GeoreferencingWindow : EditorWindow
    {
        const string Folder = "Assets/Tools/Georeferencing/Editor/";
        [SerializeField] RegionCatalog catalog;
        [SerializeField] ContentRegion selected;
        [SerializeField] GeoPoint viewCentre = new GeoPoint(41.178, -8.608);
        [SerializeField] int viewZoom = 12;
        [SerializeField] List<RegionLevel> collapsedLevels = new List<RegionLevel>();
        RegionMap map;
        VisualElement regionList, inspector;
        Label status, result;
        DoubleField latitude, longitude;
        string filter = "";
        ContentRegion lastListClick;
        double lastListClickTime = -1;
        bool showSelectionResult;

        [MenuItem("Tools/Georeferencing")]
        public static void Open() => GetWindow<GeoreferencingWindow>("Georeferencing");

        public void CreateGUI()
        {
            minSize = new Vector2(1100, 700);
            map?.Dispose();
            rootVisualElement.Clear();
            // Preserve the Editor's default theme, including its button and field styles.
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(Folder + "Georeferencing.uss");
            if (!rootVisualElement.styleSheets.Contains(sheet)) rootVisualElement.styleSheets.Add(sheet);
            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Folder + "Georeferencing.uxml").CloneTree(rootVisualElement);
            regionList = rootVisualElement.Q<ScrollView>("regionList");
            var scroll = rootVisualElement.Q<ScrollView>("inspector");
            scroll.mode = ScrollViewMode.Vertical;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            inspector = rootVisualElement.Q("inspectorContent");
            // React to edits instead of polling a hash of every coordinate on every Editor update.
            inspector.RegisterCallback<SerializedPropertyChangeEvent>(evt =>
            {
                string path = evt.changedProperty?.propertyPath ?? "";
                if (path.StartsWith("polygons", StringComparison.Ordinal) || path.StartsWith("centre", StringComparison.Ordinal)
                    || path == "boundary" || path == "radiusKilometres") map.InvalidateGeometry();
                else map.MarkDirtyRepaint();
                RefreshList();
                UpdateResult();
            });
            status = rootVisualElement.Q<Label>("status");
            result = rootVisualElement.Q<Label>("result");
            latitude = rootVisualElement.Q<DoubleField>("latitude");
            longitude = rootVisualElement.Q<DoubleField>("longitude");

            map = new RegionMap { Catalog = catalog, Selected = selected, Centre = viewCentre,
                Zoom = viewZoom, Status = message => status.text = message };
            rootVisualElement.Q("mapHost").Insert(0, map);
            map.Clicked = (point, clicks) =>
            {
                // Prefer the most specific region when filled areas overlap.
                var hit = catalog?.regions.Where(region => region != null && region.Contains(point))
                    .OrderByDescending(region => RegionLevels.Rank(region.level)).FirstOrDefault();
                lastListClick = null;
                ActivateRegion(hit, clicks >= 2, point);
            };
            latitude.SetValueWithoutNotify(map.TestPoint.latitude);
            longitude.SetValueWithoutNotify(map.TestPoint.longitude);
            rootVisualElement.Q<TextField>("filter").SetValueWithoutNotify(filter);
            rootVisualElement.Q<TextField>("filter").RegisterValueChangedCallback(evt => { filter = evt.newValue; lastListClick = null; RefreshList(); });
            var catalogField = rootVisualElement.Q<ObjectField>("catalog");
            catalogField.objectType = typeof(RegionCatalog);
            catalogField.allowSceneObjects = false;
            catalogField.SetValueWithoutNotify(catalog);
            catalogField.RegisterValueChangedCallback(evt =>
            {
                catalog = evt.newValue as RegionCatalog;
                map.Catalog = catalog;
                Select(null);
                Refresh();
                // Fit after layout, when the viewport has its final dimensions.
                map.schedule.Execute(map.FitCatalog).StartingIn(10);
            });

            BindButton("newCatalog", NewCatalog);
            BindButton("addRegion", ShowAddRegionMenu);
            BindButton("refresh", Refresh);
            BindButton("sample", CreateSample);
            BindButton("centreMap", () => { map.Centre = new GeoPoint(latitude.value, longitude.value); map.Reload(); });
            BindButton("worldMap", () => { map.Centre = new GeoPoint(0, 0); map.Zoom = 0; map.Reload(); });
            BindButton("loadMap", map.LoadVisibleTiles);
            BindButton("osmAttribution", () => Application.OpenURL("https://www.openstreetmap.org/copyright"));
            BindButton("mapData", () => Application.OpenURL("https://polygons.openstreetmap.fr/"));
            BindButton("drawPolygon", () =>
            {
                if (RegionSelection.IsFromLibrary(selected)) return;
                map.DrawPolygon = !map.DrawPolygon;
                status.text = map.DrawPolygon ? "Click Polygon Vertices, Then Apply Polygon" : "Click To Select A Region; Double-Click To Frame It";
                UpdateActions();
            });
            BindButton("clearDraft", () =>
            {
                if (RegionSelection.IsFromLibrary(selected)) return;
                map.Draft.Clear();
                map.MarkDirtyRepaint();
            });
            BindButton("applyPolygon", ApplyPolygon);
            BindButton("setCircle", SetCircle);
            BindButton("import", Import);
            BindButton("fitRegion", () => map.Fit(selected));
            BindButton("removeRegion", RemoveRegion);
            BindButton("save", AssetDatabase.SaveAssets);
            Undo.undoRedoPerformed -= HandleUndo;
            Undo.undoRedoPerformed += HandleUndo;
            Refresh();
            Select(selected);
        }

        void BindButton(string name, Action action)
        {
            rootVisualElement.Q<Button>(name).clicked += () => RunAction(action);
        }

        void RunAction(Action action)
        {
            try { action(); }
            catch (Exception error) { status.text = error.Message; }
        }

        void OpenLibrary()
        {
            var target = catalog;
            RegionLibraryWindow.Open(target, region =>
            {
                if (this == null || map == null || catalog != target || !catalog.regions.Contains(region)) return;
                ActivateRegion(region);
                Refresh();
                map.Fit(region);
            });
        }

        void ShowAddRegionMenu()
        {
            var button = rootVisualElement.Q<Button>("addRegion");
            var menu = new GenericDropdownMenu();
            menu.AddItem("Library", false, () => RunAction(OpenLibrary));
            menu.AddItem("Custom", false, () => RunAction(NewRegion));
            menu.DropDown(button.worldBound, button, DropdownMenuSizeMode.Auto);
        }

        void HandleListClick(ContentRegion region)
        {
            // Keep the click sequence across row rebuilds caused by Inspector binding events.
            double now = EditorApplication.timeSinceStartup;
            bool frame = lastListClick == region && now - lastListClickTime <= .5;
            lastListClick = frame ? null : region;
            lastListClickTime = now;
            ActivateRegion(region, frame);
        }

        void ActivateRegion(ContentRegion region, bool frame = false, GeoPoint? point = null)
        {
            showSelectionResult = !point.HasValue;
            if (point.HasValue)
            {
                latitude.SetValueWithoutNotify(point.Value.latitude);
                longitude.SetValueWithoutNotify(point.Value.longitude);
            }
            Select(region);
            UpdateResult();
            if (frame && selected != null) map.Fit(selected);
        }

        void UpdateActions()
        {
            bool hasCatalog = catalog != null;
            bool hasRegion = hasCatalog && selected != null;
            rootVisualElement.Q<Button>("addRegion").SetEnabled(hasCatalog);
            rootVisualElement.Q<Button>("save").SetEnabled(hasCatalog);
            foreach (string name in new[] { "setCircle", "import", "fitRegion", "removeRegion" })
                rootVisualElement.Q<Button>(name).SetEnabled(hasRegion);
            bool canDraw = hasRegion && !RegionSelection.IsFromLibrary(selected);
            foreach (string name in new[] { "drawPolygon", "clearDraft", "applyPolygon" })
            {
                var button = rootVisualElement.Q<Button>(name);
                button.SetEnabled(canDraw);
                button.tooltip = hasRegion && !canDraw ? "Drawing Is Available For Custom Regions" : "";
            }
            rootVisualElement.Q<Button>("drawPolygon").text = map.DrawPolygon ? "Stop Drawing" : "Draw Polygon";
            rootVisualElement.Q("regionActions").EnableInClassList("hidden", !hasRegion);
        }

        void Refresh()
        {
            map.Catalog = catalog;
            map.InvalidateGeometry();
            if (selected != null && (catalog == null || !catalog.regions.Contains(selected))) Select(null);
            if (catalog != null)
                foreach (var region in catalog.regions.Where(region => region != null && region.regionColor.a <= 0))
                {
                    Undo.RecordObject(region, "Assign Region Color");
                    region.regionColor = RegionPalette.Next(catalog);
                    EditorUtility.SetDirty(region);
                }
            RefreshList();
            UpdateActions();
            UpdateResult();
            map.Reload();
        }

        void RefreshList()
        {
            regionList.Clear();
            if (catalog == null) return;
            bool searching = !string.IsNullOrWhiteSpace(filter);
            var groups = catalog.regions.Where(region => region != null)
                .Where(region => !searching || PathLabel(region).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .GroupBy(region => region.level).OrderBy(group => RegionLevels.Rank(group.Key));
            foreach (var group in groups)
            {
                var section = new Foldout {
                    text = ObjectNames.NicifyVariableName(group.Key.ToString()) + " (" + group.Count() + ")",
                    value = searching || !collapsedLevels.Contains(group.Key), userData = group.Key
                };
                section.AddToClassList("region-level");
                section.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target != section || searching) return;
                    collapsedLevels.Remove(group.Key);
                    if (!evt.newValue) collapsedLevels.Add(group.Key);
                });
                foreach (var region in group.OrderBy(PathLabel))
                {
                    var row = new Button(() => HandleListClick(region)) {
                        text = string.IsNullOrWhiteSpace(region.displayName) ? region.name : region.displayName,
                        tooltip = PathLabel(region), userData = region
                    };
                    row.AddToClassList("region-button");
                    row.EnableInClassList("selected-region", region == selected);
                    section.Add(row);
                }
                regionList.Add(section);
            }
        }

        static string PathLabel(ContentRegion region)
        {
            var parts = new List<string>();
            var seen = new HashSet<ContentRegion>();
            for (var item = region; item != null && seen.Add(item); item = item.parent)
                parts.Insert(0, string.IsNullOrWhiteSpace(item.displayName) ? item.name : item.displayName);
            return string.Join(" / ", parts);
        }

        void Select(ContentRegion region, bool rebuildInspector = false)
        {
            // A second click only updates the result/frame; keep the bound Inspector alive.
            if (!rebuildInspector && region == selected && inspector.childCount > 0)
            {
                map.DrawPolygon = false;
                map.Draft.Clear();
                UpdateActions();
                map.MarkDirtyRepaint();
                return;
            }
            inspector.Unbind();
            inspector.Clear();
            selected = catalog != null && region != null && catalog.regions.Contains(region) ? region : null;
            if (selected != null) collapsedLevels.Remove(selected.level);
            map.Selected = selected;
            map.DrawPolygon = false;
            map.Draft.Clear();
            if (selected == null)
            {
                var help = new Label(catalog == null ? "Select Or Create A Catalogue" : "Select A Region Or Add A New Region");
                help.AddToClassList("empty-message");
                inspector.Add(help);
            }
            else
            {
                var serialized = new SerializedObject(selected);
                foreach (string property in new[] { "regionId", "displayName", "level", "parent", "regionColor", "contentIds", "boundary", "centre", "radiusKilometres", "polygons", "description", "dataSource" })
                {
                    if (property == "level")
                    {
                        // Show geographic order while keeping the old serialized enum values.
                        var choices = RegionLevels.Ordered.ToList();
                        if (!choices.Contains(selected.level)) choices.Add(selected.level);
                        var level = new PopupField<RegionLevel>("Level", choices, selected.level,
                            value => ObjectNames.NicifyVariableName(value.ToString()),
                            value => ObjectNames.NicifyVariableName(value.ToString()));
                        level.RegisterValueChangedCallback(evt =>
                        {
                            Undo.RecordObject(selected, "Change Region Level");
                            selected.level = evt.newValue;
                            EditorUtility.SetDirty(selected);
                            RefreshList();
                            UpdateResult();
                        });
                        inspector.Add(level);
                        continue;
                    }
                    if (property == "polygons")
                    {
                        // Large coordinate lists are bound only when explicitly opened for editing.
                        var foldout = new Foldout { text = "Polygon Data: " + (selected.polygons?.Count ?? 0) + " Polygons", value = false };
                        foldout.RegisterValueChangedCallback(evt =>
                        {
                            if (evt.target != foldout) return;
                            foldout.contentContainer.Unbind();
                            foldout.contentContainer.Clear();
                            if (!evt.newValue) return;
                            var data = serialized.FindProperty("polygons");
                            data.isExpanded = true;
                            var field = new PropertyField(data);
                            foldout.Add(field);
                            field.Bind(serialized);
                        });
                        inspector.Add(foldout);
                        continue;
                    }
                    string label = property == "dataSource" ? "Boundary Data Source / Licence" : null;
                    inspector.Add(label == null ? new PropertyField(serialized.FindProperty(property)) : new PropertyField(serialized.FindProperty(property), label));
                }
                inspector.Bind(serialized);
                Selection.activeObject = selected;
            }
            // Keep row elements alive so a second click is recognised as a double-click.
            foreach (var row in regionList.Query<Button>(className: "region-button").ToList())
                row.EnableInClassList("selected-region", row.userData as ContentRegion == selected);
            if (selected != null)
                foreach (var section in regionList.Query<Foldout>().ToList())
                    if (section.userData is RegionLevel level && level == selected.level) section.value = true;
            UpdateActions();
            map.MarkDirtyRepaint();
        }

        static string EnsureFolder(string path)
        {
            path = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return path;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            return path;
        }

        internal static string RegionsFolder(RegionCatalog value)
        {
            string path = AssetDatabase.GetAssetPath(value);
            string folder = Path.GetDirectoryName(path).Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(path);
            // Existing catalogues can remain where they are, with a dedicated sibling folder.
            if (Path.GetFileName(folder) != name) folder += "/" + name;
            return EnsureFolder(folder + "/Regions");
        }

        RegionCatalog CreateCatalog(string defaultName)
        {
            string root = EnsureFolder("Assets/Tools/Georeferencing/Catalogues");
            string path = EditorUtility.SaveFilePanelInProject("Create Catalogue", defaultName, "asset", "Choose A Name; A Dedicated Catalogue Folder Will Be Created", root);
            if (string.IsNullOrEmpty(path)) return null;
            string name = Path.GetFileNameWithoutExtension(path);
            string folder = AssetDatabase.GenerateUniqueAssetPath(Path.GetDirectoryName(path).Replace('\\', '/') + "/" + name);
            EnsureFolder(folder);
            var value = CreateInstance<RegionCatalog>();
            AssetDatabase.CreateAsset(value, folder + "/" + name + ".asset");
            RegionsFolder(value);
            return value;
        }

        void NewCatalog()
        {
            var value = CreateCatalog("RegionCatalogue");
            if (value == null) return;
            catalog = value;
            selected = null;
            CreateGUI();
        }

        void NewRegion()
        {
            if (catalog == null) return;
            string path = EditorUtility.SaveFilePanelInProject("Create Content Region", "ContentRegion", "asset", "Choose A Region Name", RegionsFolder(catalog));
            if (string.IsNullOrEmpty(path)) return;
            var region = CreateInstance<ContentRegion>();
            region.regionId = Guid.NewGuid().ToString("N");
            region.displayName = Path.GetFileNameWithoutExtension(path);
            region.parent = selected != null && selected.level == RegionLevel.CustomZone ? selected.parent : selected;
            region.level = region.parent == null ? RegionLevel.Continent : RegionLevels.Next(region.parent.level);
            region.centre = map.TestPoint;
            region.regionColor = RegionPalette.Next(catalog);
            AssetDatabase.CreateAsset(region, AssetDatabase.GenerateUniqueAssetPath(path));
            Undo.RecordObject(catalog, "Add Region");
            catalog.regions.Add(region);
            EditorUtility.SetDirty(catalog);
            ActivateRegion(region);
            Refresh();
        }

        void RemoveRegion()
        {
            if (catalog == null || selected == null) return;
            // Include descendants so the remaining catalogue has no missing parent references.
            var branch = new HashSet<ContentRegion> { selected };
            bool changed;
            do
            {
                changed = false;
                foreach (var region in catalog.regions)
                    if (region != null && region.parent != null && branch.Contains(region.parent))
                        changed |= branch.Add(region);
            } while (changed);
            string message = "Remove " + (selected.displayName ?? selected.name)
                + (branch.Count > 1 ? " And " + (branch.Count - 1) + " Subregions" : "")
                + " From This Catalogue?\n\nThe Region Assets Are Kept. Undo Restores The Catalogue Entries.";
            if (!EditorUtility.DisplayDialog("Remove Region", message, "Remove", "Cancel")) return;
            Undo.RecordObject(catalog, "Remove Catalogue Regions");
            catalog.regions.RemoveAll(branch.Contains);
            EditorUtility.SetDirty(catalog);
            Select(null);
            Refresh();
        }

        void Test()
        {
            showSelectionResult = false;
            if (catalog == null) { result.text = "Assign A Catalogue To Test Content"; return; }
            var point = new GeoPoint(latitude.value, longitude.value);
            map.TestPoint = point;
            ShowResult(RegionResolver.Resolve(catalog, point));
            map.MarkDirtyRepaint();
        }

        void UpdateResult()
        {
            if (!showSelectionResult) { Test(); return; }
            if (selected == null) { result.text = "Select A Region Or Click The Map"; return; }
            ShowResult(RegionSelection.Resolve(catalog, selected));
        }

        void ShowResult(RegionResult resolved)
        {
            result.text = resolved.IsValid
                ? "Regions: " + string.Join(", ", resolved.Regions.Select(region => region.displayName))
                    + "\nContent: " + string.Join(", ", resolved.ContentIds)
                : string.Join("\n", resolved.Errors.Distinct());
        }

        void ApplyPolygon()
        {
            if (RegionSelection.IsFromLibrary(selected)) return;
            if (selected == null || map.Draft.Count < 3) throw new InvalidOperationException("Draw At Least Three Vertices");
            var outline = new GeoRing { points = new List<GeoPoint>(map.Draft) };
            if (!GeoGeometry.ValidRing(outline)) throw new InvalidOperationException("The Polygon Needs Three Distinct Non-Collinear Vertices");
            Undo.RecordObject(selected, "Set Region Polygon");
            selected.boundary = RegionBoundary.Polygon;
            selected.polygons = new List<GeoPolygon> { new GeoPolygon { outline = outline } };
            EditorUtility.SetDirty(selected);
            map.InvalidateGeometry();
            Select(selected, true);
            UpdateResult();
        }

        void SetCircle()
        {
            if (selected == null) return;
            var point = new GeoPoint(latitude.value, longitude.value);
            if (!point.IsValid) throw new InvalidOperationException("Enter Valid Test Coordinates First");
            Undo.RecordObject(selected, "Set Region Circle");
            selected.boundary = RegionBoundary.Circle;
            selected.centre = point;
            EditorUtility.SetDirty(selected);
            map.InvalidateGeometry();
            Select(selected, true);
            UpdateResult();
        }

        void Import()
        {
            if (selected == null) return;
            string path = EditorUtility.OpenFilePanel("Import Polygon GeoJSON", "", "geojson,json");
            if (string.IsNullOrEmpty(path)) return;
            var polygons = GeoJsonImporter.Parse(File.ReadAllText(path));
            Undo.RecordObject(selected, "Import Region Boundary");
            selected.polygons = polygons;
            selected.boundary = RegionBoundary.Polygon;
            selected.dataSource = "Imported GeoJSON: " + Path.GetFileName(path) + "; add provider and licence here";
            EditorUtility.SetDirty(selected);
            map.InvalidateGeometry();
            Select(selected, true);
            map.Fit(selected);
            UpdateResult();
        }

        void CreateSample()
        {
            var value = CreateCatalog("DemoRegions");
            if (value == null) return;
            var template = AssetDatabase.LoadAssetAtPath<RegionCatalog>("Assets/Tools/Georeferencing/Examples/DemoRegions/DemoRegions.asset");
            if (template == null) throw new InvalidOperationException("The Included Demo Catalogue Is Missing");
            catalog = value;
            var copies = new Dictionary<ContentRegion, ContentRegion>();
            foreach (var region in template.regions)
            {
                var copy = Instantiate(region);
                copy.regionId = Guid.NewGuid().ToString("N");
                copy.regionColor = RegionPalette.Next(catalog);
                AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(RegionsFolder(catalog) + "/" + region.name + ".asset"));
                catalog.regions.Add(copy);
                copies.Add(region, copy);
            }
            foreach (var pair in copies)
            {
                pair.Value.parent = pair.Key.parent != null ? copies[pair.Key.parent] : null;
                EditorUtility.SetDirty(pair.Value);
            }
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            selected = null;
            CreateGUI();
            map.schedule.Execute(map.FitCatalog).StartingIn(10);
        }

        void HandleUndo() { if (map != null) Refresh(); }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= HandleUndo;
            if (map == null) return;
            viewCentre = map.Centre;
            viewZoom = map.Zoom;
            map.Dispose();
        }
    }
}

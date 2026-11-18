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
        const string DemoFolder = "Assets/Tools/Georeferencing/Examples/DemoRegions/";
        const string DemoCatalogPath = DemoFolder + "DemoRegions.asset";
        bool IsDemoCatalog => catalog != null && AssetDatabase.GetAssetPath(catalog).StartsWith(DemoFolder, StringComparison.Ordinal);
        [SerializeField] RegionCatalog catalog;
        [SerializeField] ContentRegion selected;
        [SerializeField] GeoPoint viewCentre = new GeoPoint(41.178, -8.608);
        [SerializeField] int viewZoom = 12;
        [SerializeField] List<RegionLevel> collapsedLevels = new List<RegionLevel>();
        [SerializeField] bool applyLocationRules;
        [SerializeField] LocationOrigin testOrigin = LocationOrigin.IPAddress;
        [SerializeField] LocationPrecision testPrecision = LocationPrecision.Unknown;
        [SerializeField] double testAccuracyMetres = 100;
        RegionMap map;
        VisualElement regionList, inspector;
        Label status, result;
        DoubleField latitude, longitude;
        string filter = "";
        ContentRegion lastListClick;
        double lastListClickTime = -1;
        bool showSelectionResult;
        bool pendingRegionFrame;

        [MenuItem("Tools/Georeferencing")]
        public static void Open() => GetWindow<GeoreferencingWindow>("Georeferencing");

        public static void ShowRegion(RegionCatalog source, ContentRegion region)
        {
            if (source == null || region == null || source.regions == null || !source.regions.Contains(region)) return;
            var window = GetWindow<GeoreferencingWindow>("Georeferencing");
            window.catalog = source;
            window.selected = region;
            window.pendingRegionFrame = true;
            window.Show();
            window.Focus();
            window.rootVisualElement.schedule.Execute(window.ApplyPendingRegion).StartingIn(20);
        }

        void ApplyPendingRegion()
        {
            // Opening another tool can finish before its viewport is laid out. GeometryChanged retries once ready.
            if (!pendingRegionFrame || map == null || !(map.contentRect.width > 0) || !(map.contentRect.height > 0)) return;
            pendingRegionFrame = false;
            map.Catalog = catalog;
            map.InvalidateGeometry();
            rootVisualElement.Q<ObjectField>("catalog").SetValueWithoutNotify(catalog);
            RefreshList();
            Select(selected, true);
            ActivateRegion(selected, true);
        }

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
            var rulesToggle = rootVisualElement.Q<Toggle>("applyLocationRules");
            rulesToggle.SetValueWithoutNotify(applyLocationRules);
            rulesToggle.RegisterValueChangedCallback(evt => { applyLocationRules = evt.newValue; UpdateResult(); });
            var accuracyField = rootVisualElement.Q<DoubleField>("testAccuracy");
            accuracyField.SetValueWithoutNotify(testAccuracyMetres);
            accuracyField.RegisterValueChangedCallback(evt => { testAccuracyMetres = evt.newValue; UpdateResult(); });
            var originField = rootVisualElement.Q<EnumField>("testOrigin");
            originField.Init(testOrigin);
            originField.RegisterValueChangedCallback(evt => { testOrigin = (LocationOrigin)evt.newValue; UpdateResult(); });
            var precisionField = rootVisualElement.Q<EnumField>("testPrecision");
            precisionField.Init(testPrecision);
            precisionField.RegisterValueChangedCallback(evt => { testPrecision = (LocationPrecision)evt.newValue; UpdateResult(); });

            map = new RegionMap { Catalog = catalog, Selected = selected, Centre = viewCentre,
                Zoom = viewZoom, Status = message => status.text = message };
            map.RegisterCallback<GeometryChangedEvent>(_ => ApplyPendingRegion());
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
            // Typed coordinates and map clicks use the same test point and content query.
            latitude.RegisterValueChangedCallback(_ => Test());
            longitude.RegisterValueChangedCallback(_ => Test());
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
            BindButton("populateDemo", PopulateFromDemo);
            BindButton("locationRules", EditLocationRules);
            BindButton("centreMap", CentreMap);
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
            bool canEdit = hasCatalog && !IsDemoCatalog;
            inspector.SetEnabled(canEdit);
            foreach (string name in new[] { "addRegion", "save", "locationRules", "populateDemo" })
                rootVisualElement.Q<Button>(name).SetEnabled(canEdit);
            rootVisualElement.Q<Button>("fitRegion").SetEnabled(hasRegion);
            foreach (string name in new[] { "setCircle", "import", "removeRegion" })
                rootVisualElement.Q<Button>(name).SetEnabled(canEdit && hasRegion);
            bool canDraw = canEdit && hasRegion && !RegionSelection.IsFromLibrary(selected);
            foreach (string name in new[] { "drawPolygon", "clearDraft", "applyPolygon" })
            {
                var button = rootVisualElement.Q<Button>(name);
                button.SetEnabled(canDraw);
                button.tooltip = IsDemoCatalog ? "The Demo Is Read-Only; Populate An Editable Catalogue"
                    : hasRegion && !canDraw ? "Drawing Is Available For Custom Regions" : "";
            }
            rootVisualElement.Q<Button>("drawPolygon").text = map.DrawPolygon ? "Stop Drawing" : "Draw Polygon";
            rootVisualElement.Q("regionActions").EnableInClassList("hidden", !hasRegion);
        }

        void Refresh()
        {
            map.Catalog = catalog;
            map.InvalidateGeometry();
            if (selected != null && (catalog == null || !catalog.regions.Contains(selected))) Select(null);
            if (catalog != null && !IsDemoCatalog)
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
                foreach (string property in new[] { "regionId", "displayName", "level", "parent", "regionColor", "contentIds", "maximumAccuracyMetres", "boundary", "centre", "radiusKilometres", "polygons", "description", "dataSource" })
                {
                    if (property == "contentIds")
                    {
                        var regionForContent = selected;
                        inspector.Add(new Button(() => Tools.Content.Editor.ContentBrowserWindow.ChooseForRegion(catalog, regionForContent))
                        { text = "Choose Content", tooltip = "Browse Content Thumbnails And Assign IDs To This Region" });
                    }
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
            value.locationRules = CreateRules(folder);
            EditorUtility.SetDirty(value);
            RegionsFolder(value);
            return value;
        }

        static LocationRules CreateRules(string folder)
        {
            var rules = CreateInstance<LocationRules>();
            AssetDatabase.CreateAsset(rules, AssetDatabase.GenerateUniqueAssetPath(folder + "/LocationRules.asset"));
            return rules;
        }

        void EditLocationRules()
        {
            if (catalog == null) return;
            if (catalog.locationRules == null)
            {
                Undo.RecordObject(catalog, "Assign Location Rules");
                catalog.locationRules = CreateRules(Path.GetDirectoryName(AssetDatabase.GetAssetPath(catalog)).Replace('\\', '/'));
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
            }
            Selection.activeObject = catalog.locationRules;
            EditorGUIUtility.PingObject(catalog.locationRules);
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
            var point = new GeoPoint(latitude.value, longitude.value);
            map.TestPoint = point;
            map.MarkDirtyRepaint();
            if (!point.IsValid)
            {
                result.text = "Latitude Must Be Between -90 And 90; Longitude Between -180 And 180";
                return;
            }
            if (catalog == null) { result.text = "Assign A Catalogue To Test Content"; return; }
            ShowResult(RegionResolver.Resolve(catalog, point));
        }

        void CentreMap()
        {
            // Invalid coordinates must not reach the projection or leave a misleading old marker.
            Test();
            if (!map.TestPoint.IsValid) return;
            map.Centre = map.TestPoint;
            map.Reload();
        }

        void UpdateResult()
        {
            if (!showSelectionResult) { Test(); return; }
            if (selected == null) { result.text = "Select A Region Or Click The Map"; return; }
            ShowResult(RegionSelection.Resolve(catalog, selected));
        }

        void ShowResult(RegionResult resolved)
        {
            string ruleInfo = "";
            if (applyLocationRules)
            {
                resolved = RegionResolver.ApplyLocationRules(catalog, resolved, testOrigin, testPrecision, testAccuracyMetres);
                ruleInfo = catalog?.locationRules == null ? "Location Rules: Not Assigned (Unfiltered)\n"
                    : catalog.locationRules.TryGetMinimumLevel(testOrigin, testPrecision, testAccuracyMetres, out var level)
                        ? $"Accuracy: {testAccuracyMetres} m; Default Minimum Level: {level} (Region Overrides Apply)\n"
                        : $"Source: {testOrigin}; No Eligible Level\n";
            }
            result.text = resolved.IsValid
                ? ruleInfo + "Regions: " + string.Join(", ", resolved.Regions.Select(region => region.displayName))
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

        void PopulateFromDemo()
        {
            if (catalog == null || IsDemoCatalog) return;
            var template = AssetDatabase.LoadAssetAtPath<RegionCatalog>(DemoCatalogPath);
            var errors = new List<string>();
            RegionResolver.Validate(template, errors);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
            if (template.locationRules == null) throw new InvalidOperationException("The Demo Has No Location Rules");
            if (catalog.regions.Count > 0 && !EditorUtility.DisplayDialog("Populate From Demo",
                "Replace the regions and location rules in " + catalog.name
                + " with the demo base? Undo is available.", "Replace", "Cancel")) return;

            string folder = RegionsFolder(catalog);
            string catalogueFolder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(catalog)).Replace('\\', '/');
            var existing = catalog.regions.Where(region => region != null).ToList();
            var copies = new Dictionary<ContentRegion, ContentRegion>();
            var sharedRegions = new HashSet<ContentRegion>();
            var sharedRules = new HashSet<LocationRules>();
            foreach (string guid in AssetDatabase.FindAssets("t:RegionCatalog"))
            {
                var other = AssetDatabase.LoadAssetAtPath<RegionCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                if (other == null || other == catalog) continue;
                if (other.locationRules != null) sharedRules.Add(other.locationRules);
                if (other.regions != null)
                    foreach (var region in other.regions)
                        if (region != null) sharedRegions.Add(region);
            }
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Populate Catalogue From Demo");
            Undo.RecordObject(catalog, "Populate Catalogue From Demo");

            // Reuse owned assets on repeated population; never overwrite shared or demo assets.
            if (catalog.locationRules == null || sharedRules.Contains(catalog.locationRules)
                || !AssetDatabase.GetAssetPath(catalog.locationRules).StartsWith(catalogueFolder + "/", StringComparison.Ordinal))
            {
                catalog.locationRules = CreateRules(catalogueFolder);
                Undo.RegisterCreatedObjectUndo(catalog.locationRules, "Create Catalogue Rules");
            }
            else Undo.RecordObject(catalog.locationRules, "Populate Location Rules");
            EditorUtility.CopySerialized(template.locationRules, catalog.locationRules);
            EditorUtility.SetDirty(catalog.locationRules);

            foreach (var region in template.regions)
            {
                var copy = existing.FirstOrDefault(candidate => !sharedRegions.Contains(candidate) && !copies.ContainsValue(candidate)
                    && (candidate.regionId == region.regionId || candidate.name == region.name)
                    && AssetDatabase.GetAssetPath(candidate).StartsWith(folder + "/", StringComparison.Ordinal));
                if (copy == null)
                {
                    copy = CreateInstance<ContentRegion>();
                    AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + region.name + ".asset"));
                    Undo.RegisterCreatedObjectUndo(copy, "Create Catalogue Region");
                }
                else Undo.RecordObject(copy, "Populate Catalogue Region");
                EditorUtility.CopySerialized(region, copy);
                copies.Add(region, copy);
            }

            // Remap every parent after all copies exist, preserving the complete hierarchy.
            catalog.regions.Clear();
            foreach (var region in template.regions)
            {
                var copy = copies[region];
                copy.parent = region.parent != null ? copies[region.parent] : null;
                catalog.regions.Add(copy);
                EditorUtility.SetDirty(copy);
            }
            EditorUtility.SetDirty(catalog);
            Undo.CollapseUndoOperations(undoGroup);
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

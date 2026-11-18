using System;
using System.Collections.Generic;
using System.Linq;
using Tools.Georeferencing;
using Tools.Georeferencing.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tools.Content.Editor
{
    public sealed class ContentBrowserWindow : EditorWindow
    {
        const string Folder = "Assets/Tools/Content/Editor/";
        const string DemoFolder = "Assets/Tools/Georeferencing/Examples/DemoRegions/";
        const string Unclassified = "__unclassified";
        static readonly string[] DefaultCategories = { "cards", "enemies", "rooms", "maps" };

        [SerializeField] ContentCatalog catalog;
        [SerializeField] string category = "", search = "", relatedTo = "";
        [SerializeField] int selectedIndex = -1, requirementFor = -1;
        [SerializeField] ContentRegion assignmentRegion;
        [SerializeField] RegionCatalog assignmentCatalog;
        [SerializeField] bool regionSectionOpen = true;
        [SerializeField] bool allowedCodesOpen = true, excludedCodesOpen = true;
        [SerializeField] ContentRegion previewRegion;
        [SerializeField] RegionLevel regionMapLevel = RegionLevel.Country;
        [SerializeField] GeoPoint regionMapCentre = new GeoPoint(40, -8);
        [SerializeField] int regionMapZoom = 5;
        RegionMap policyMap;
        RegionCatalog mapCatalog;
        bool fitPolicyMap;
        readonly List<RegionLink> regions = new List<RegionLink>();
        readonly List<(Image image, Object asset)> previews = new List<(Image, Object)>();
        readonly Dictionary<Object, (string title, Texture art)> presentation = new Dictionary<Object, (string, Texture)>();
        VisualElement categories, grid, details;
        Label summary, status, contextLabel;
        Label regionPreviewValue;
        IVisualElementScheduledItem previewTask;
        bool refreshQueued, reloadQueued;
        ContentCatalog.Entry lastClicked;
        double lastClickTime = -1;

        sealed class RegionLink
        {
            public RegionCatalog catalog;
            public ContentRegion region;
        }

        ContentCatalog.Entry Selected => EntryAt(selectedIndex);
        List<ContentCatalog.Entry> Entries => catalog != null ? catalog.entries : null;
        static bool IsDemo(ContentRegion region) => region != null
            && AssetDatabase.GetAssetPath(region).StartsWith(DemoFolder, StringComparison.Ordinal);

        [MenuItem("Tools/Content")]
        public static void Open() => GetWindow<ContentBrowserWindow>("Content");

        public static void Open(ContentCatalog source)
        {
            var window = GetWindow<ContentBrowserWindow>("Content");
            window.catalog = source;
            window.selectedIndex = -1;
            window.assignmentRegion = null;
            window.requirementFor = -1;
            window.relatedTo = window.category = window.search = "";
            window.SyncCatalogueField();
            window.SyncSearch();
            window.QueueRefresh(true);
        }

        public static void ChooseForRegion(RegionCatalog source, ContentRegion region)
        {
            if (region == null || IsDemo(region)) return;
            var window = GetWindow<ContentBrowserWindow>("Content");
            window.assignmentRegion = region;
            window.assignmentCatalog = source;
            window.requirementFor = -1;
            window.relatedTo = window.category = window.search = "";
            window.SyncSearch();
            window.QueueRefresh(true);
        }

        void OnEnable()
        {
            Undo.undoRedoPerformed += Reload;
            EditorApplication.projectChanged += Reload;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= Reload;
            EditorApplication.projectChanged -= Reload;
            previewTask?.Pause();
            DisposePolicyMap();
            refreshQueued = reloadQueued = false;
        }

        public void CreateGUI()
        {
            DisposePolicyMap();
            minSize = new Vector2(820, 620);
            rootVisualElement.Clear();
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(Folder + "ContentBrowser.uss");
            if (!rootVisualElement.styleSheets.Contains(sheet)) rootVisualElement.styleSheets.Add(sheet);
            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Folder + "ContentBrowser.uxml").CloneTree(rootVisualElement);
            categories = rootVisualElement.Q("categories");
            grid = rootVisualElement.Q("grid");
            details = rootVisualElement.Q("details");
            summary = rootVisualElement.Q<Label>("summary");
            status = rootVisualElement.Q<Label>("status");
            contextLabel = rootVisualElement.Q<Label>("contextLabel");
            rootVisualElement.Q<TwoPaneSplitView>("contentSplit").viewDataKey = "content-split";
            if (catalog == null) catalog = Resources.Load<ContentCatalog>(ContentCatalog.ResourcePath);
            var field = rootVisualElement.Q<ObjectField>("catalog");
            field.objectType = typeof(ContentCatalog);
            field.allowSceneObjects = false;
            SyncCatalogueField();
            field.RegisterValueChangedCallback(evt =>
            {
                catalog = evt.newValue as ContentCatalog;
                selectedIndex = requirementFor = -1;
                relatedTo = category = "";
                QueueRefresh(true);
            });
            var geographic = rootVisualElement.Q<ObjectField>("geographicCatalog");
            geographic.objectType = typeof(RegionCatalog);
            geographic.allowSceneObjects = false;
            geographic.RegisterValueChangedCallback(evt =>
            {
                if (catalog == null || catalog.regionCatalog == evt.newValue) return;
                Undo.RecordObject(catalog, "Change Geographic Catalogue");
                catalog.SetRegionCatalog(evt.newValue as RegionCatalog);
                EditorUtility.SetDirty(catalog);
                QueueRefresh(true);
            });
            SyncSearch();
            rootVisualElement.Q<ToolbarSearchField>("search").RegisterValueChangedCallback(evt =>
            { search = evt.newValue; QueueRefresh(); });
            rootVisualElement.Q<Button>("add").clicked += ShowAddMenu;
            rootVisualElement.Q<Button>("save").clicked += () => { AssetDatabase.SaveAssets(); QueueRefresh(true); };
            rootVisualElement.Q<Button>("refresh").clicked += Reload;
            rootVisualElement.Q<Button>("clearContext").clicked += () =>
            {
                if (requirementFor >= 0) requirementFor = -1;
                else if (!string.IsNullOrEmpty(relatedTo)) relatedTo = "";
                else assignmentRegion = null;
                QueueRefresh();
            };
            foreach (string name in new[] { "categories", "contentScroll", "detailScroll" })
                rootVisualElement.Q<ScrollView>(name).horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            LoadRegions();
            RefreshView();
        }

        void SyncCatalogueField() => rootVisualElement.Q<ObjectField>("catalog")?.SetValueWithoutNotify(catalog);
        void SyncSearch() => rootVisualElement.Q<ToolbarSearchField>("search")?.SetValueWithoutNotify(search);
        void Reload() => QueueRefresh(true);

        void QueueRefresh(bool reload = false)
        {
            reloadQueued |= reload;
            if (refreshQueued || grid == null) return;
            refreshQueued = true;
            rootVisualElement.schedule.Execute(() =>
            {
                refreshQueued = false;
                if (reloadQueued) { LoadRegions(); presentation.Clear(); }
                reloadQueued = false;
                RefreshView();
            });
        }

        void LoadRegions()
        {
            // Inspect region assignments only on refresh, never on preview ticks or each keystroke.
            regions.Clear();
            var source = catalog != null ? catalog.regionCatalog : null;
            if (source != null && source.regions != null)
            {
                foreach (var region in source.regions.Where(item => item != null).Distinct())
                    regions.Add(new RegionLink { catalog = source, region = region });
            }
        }

        ContentCatalog.Entry EntryAt(int index) => Entries != null && index >= 0 && index < Entries.Count ? Entries[index] : null;
        ContentCatalog.Entry Find(string id) => Entries?.FirstOrDefault(entry => entry != null && entry.id == id);
        static string CategoryToken(string value) => string.IsNullOrWhiteSpace(value) ? Unclassified : value;
        static string CategoryName(string value) => string.IsNullOrWhiteSpace(value) || value == Unclassified ? "Unclassified" : ObjectNames.NicifyVariableName(value);

        (string title, Texture art) Presentation(ContentCatalog.Entry entry)
        {
            if (entry?.asset == null) return (entry?.id ?? "Missing Entry", null);
            if (presentation.TryGetValue(entry.asset, out var cached)) return cached;
            string title = entry.asset.name;
            Texture art = null;
            if (entry.asset is ScriptableObject)
            {
                // Read optional presentation fields without depending on gameplay card classes.
                using (var serialized = new SerializedObject(entry.asset))
                {
                    var name = serialized.FindProperty("displayName");
                    if (name?.propertyType == SerializedPropertyType.String && !string.IsNullOrWhiteSpace(name.stringValue)) title = name.stringValue;
                    var image = serialized.FindProperty("frontArt");
                    if (image?.propertyType == SerializedPropertyType.ObjectReference) art = image.objectReferenceValue as Texture;
                }
            }
            return presentation[entry.asset] = (title, art);
        }

        Image Thumbnail(ContentCatalog.Entry entry, string className)
        {
            var image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.AddToClassList(className);
            var art = Presentation(entry).art;
            image.image = art != null ? art : entry.asset == null ? EditorGUIUtility.ObjectContent(null, typeof(Object)).image
                : AssetPreview.GetAssetPreview(entry.asset) ?? AssetPreview.GetMiniThumbnail(entry.asset);
            if (art == null && entry.asset != null) previews.Add((image, entry.asset));
            return image;
        }

        bool Matches(ContentCatalog.Entry entry)
        {
            if (entry == null || (!string.IsNullOrEmpty(category) && CategoryToken(entry.category) != category)) return false;
            if (!string.IsNullOrEmpty(relatedTo) && entry.id != relatedTo
                && !(entry.requiredIds?.Contains(relatedTo) ?? false)
                && !(Find(relatedTo)?.requiredIds?.Contains(entry.id) ?? false)) return false;
            if (string.IsNullOrWhiteSpace(search)) return true;
            var terms = new List<string> { entry.id, entry.category, Presentation(entry).title, entry.asset != null ? entry.asset.name : "" };
            foreach (string id in entry.requiredIds ?? new List<string>())
            { terms.Add(id); if (Find(id) is ContentCatalog.Entry required) terms.Add(Presentation(required).title); }
            foreach (var link in regions.Where(link => entry.allowedRegionIds?.Contains(link.region.regionId) == true
                || entry.excludedRegionIds?.Contains(link.region.regionId) == true))
            { terms.Add(link.catalog.name); terms.Add(link.region.regionId); terms.Add(link.region.displayName); }
            return search.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .All(word => terms.Any(term => term?.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        void RefreshView()
        {
            if (grid == null) return;
            previewTask?.Pause();
            previews.Clear();
            regionPreviewValue = null;
            // Keep the map and its geometry/tile caches while the entry's controls are rebuilt.
            policyMap?.RemoveFromHierarchy();
            categories.Clear(); grid.Clear(); details.Clear();
            rootVisualElement.Q<Button>("add").SetEnabled(catalog != null);
            rootVisualElement.Q<Button>("save").SetEnabled(catalog != null);
            var geographic = rootVisualElement.Q<ObjectField>("geographicCatalog");
            geographic.SetValueWithoutNotify(catalog != null ? catalog.regionCatalog : null);
            geographic.SetEnabled(catalog != null);
            var entries = Entries ?? new List<ContentCatalog.Entry>();
            var groups = DefaultCategories.Concat(entries.Where(entry => entry != null).Select(entry => entry.category)).Select(CategoryToken).Distinct().ToList();
            AddCategory("", "All Content", entries.Count);
            foreach (string group in groups) AddCategory(CategoryToken(group), CategoryName(group), entries.Count(entry => entry != null && CategoryToken(entry.category) == CategoryToken(group)));
            int visible = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!Matches(entry)) continue;
                int index = i;
                var tile = new Button(() => ClickEntry(index)) { tooltip = entry.id + "\nDouble-Click To Locate Asset" };
                tile.AddToClassList("content-tile");
                tile.EnableInClassList("is-selected", selectedIndex == index);
                tile.Add(Thumbnail(entry, "tile-image"));
                AddLabel(tile, Presentation(entry).title, "tile-name");
                AddLabel(tile, entry.id, "tile-id");
                if (requirementFor >= 0) tile.SetEnabled(CanRequire(EntryAt(requirementFor), entry));
                grid.Add(tile);
                visible++;
            }
            summary.text = (string.IsNullOrEmpty(category) ? "All Content" : CategoryName(category)) + " (" + visible + ")";
            if (visible == 0) AddLabel(grid, catalog == null ? "Choose A Content Catalogue" : "No Matching Content", "muted");
            BuildDetails();
            if (policyMap != null && policyMap.parent == null) DisposePolicyMap();
            UpdateContext();
            var errors = catalog != null ? catalog.Validate() : new List<string>();
            status.text = errors.Count > 0 ? string.Join("; ", errors.Take(3)) : "Select Content To Explore Its Requirements, Uses And Regions.";
            status.tooltip = string.Join("\n", errors);
            StartPreviews();
        }

        void AddCategory(string value, string label, int count)
        {
            var button = new Button(() => { category = value; QueueRefresh(); }) { text = label + " (" + count + ")" };
            button.AddToClassList("category-button");
            button.EnableInClassList("is-selected", category == value);
            categories.Add(button);
        }

        static Label AddLabel(VisualElement parent, string text, string className)
        {
            var label = new Label(text ?? "");
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        static void AddDivider(VisualElement parent)
        {
            var line = new VisualElement();
            line.AddToClassList("horizontal-divider");
            parent.Add(line);
        }

        void UpdateContext()
        {
            string text = requirementFor >= 0 ? "Choosing Required Content For: " + (EntryAt(requirementFor)?.id ?? "")
                : !string.IsNullOrEmpty(relatedTo) ? "Related To: " + relatedTo
                : assignmentRegion != null ? "Assigning Content To: " + assignmentRegion.displayName : "";
            contextLabel.text = text;
            rootVisualElement.Q("context").style.display = text.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            rootVisualElement.Q<Button>("clearContext").text = "Done";
        }

        void ClickEntry(int index)
        {
            var entry = EntryAt(index);
            double now = EditorApplication.timeSinceStartup;
            // Rebuilding tiles after selection resets native click counts, so retain the last clicked entry.
            if (requirementFor < 0 && entry == lastClicked && now - lastClickTime <= .5)
            {
                if (entry?.asset != null) EditorGUIUtility.PingObject(entry.asset);
                lastClicked = null;
                return;
            }
            lastClicked = entry; lastClickTime = now;
            Choose(index);
        }

        void Choose(int index)
        {
            var candidate = EntryAt(index);
            if (requirementFor >= 0)
            {
                var owner = EntryAt(requirementFor);
                if (!CanRequire(owner, candidate)) return;
                selectedIndex = requirementFor;
                Edit("Add Required Content", entry => { entry.requiredIds ??= new List<string>(); entry.requiredIds.Add(candidate.id); });
            }
            else { selectedIndex = index; QueueRefresh(); }
        }

        bool CanRequire(ContentCatalog.Entry owner, ContentCatalog.Entry candidate)
        {
            if (owner == null || owner.category == "cards" || candidate == null || string.IsNullOrWhiteSpace(owner.id) || string.IsNullOrWhiteSpace(candidate.id)
                || owner.id == candidate.id || owner.requiredIds?.Contains(candidate.id) == true) return false;
            var pending = new Stack<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            pending.Push(candidate.id);
            while (pending.Count > 0)
            {
                string id = pending.Pop();
                if (id == owner.id) return false;
                if (!seen.Add(id)) continue;
                foreach (string required in Find(id)?.requiredIds ?? new List<string>())
                    if (required != null) pending.Push(required);
            }
            return true;
        }

        void Edit(string label, Action<ContentCatalog.Entry> change)
        {
            if (Selected == null) return;
            Undo.RecordObject(catalog, label);
            change(Selected);
            EditorUtility.SetDirty(catalog);
            presentation.Clear();
            QueueRefresh();
        }

        void BuildDetails()
        {
            var entry = Selected;
            if (entry == null)
            {
                AddLabel(details, "Select Content To See Its Details", "muted");
                return;
            }
            AddLabel(details, Presentation(entry).title, "detail-title");
            details.Add(Thumbnail(entry, "detail-image"));
            var id = new TextField("ID") { value = entry.id, isDelayed = true,
                tooltip = "Stable ID. Update references in regions and requirements if you rename it." };
            id.RegisterValueChangedCallback(evt => Edit("Change Content ID", item => item.id = evt.newValue));
            details.Add(id);
            var choices = DefaultCategories.ToList();
            if (!choices.Contains(entry.category)) choices.Add(entry.category ?? "");
            var classification = new PopupField<string>("Category", choices, entry.category ?? "", CategoryName, CategoryName);
            classification.AddToClassList("category-field");
            classification.RegisterValueChangedCallback(evt => Edit("Change Content Category", item =>
            {
                item.category = evt.newValue;
                if (item.category == "cards") item.requiredIds = new List<string>();
            }));
            details.Add(classification);
            var asset = new ObjectField("Asset") { objectType = entry.category == "rooms" || entry.category == "enemies" ? typeof(GameObject) : typeof(Object),
                allowSceneObjects = false, value = entry.asset };
            asset.RegisterValueChangedCallback(evt => Edit("Change Content Asset", item => item.asset = evt.newValue));
            details.Add(asset);
            var actions = new VisualElement(); actions.AddToClassList("detail-actions"); details.Add(actions);
            var ping = new Button(() => EditorGUIUtility.PingObject(entry.asset)) { text = "Locate Asset" };
            ping.SetEnabled(entry.asset != null); actions.Add(ping);
            var related = new Button(() =>
            {
                relatedTo = string.IsNullOrEmpty(relatedTo) ? entry.id : "";
                category = search = ""; SyncSearch(); QueueRefresh();
            }) { text = "Show Related" };
            related.EnableInClassList("is-selected", !string.IsNullOrEmpty(relatedTo));
            related.SetEnabled(!string.IsNullOrWhiteSpace(entry.id));
            if (entry.category != "cards") actions.Add(related);
            if (assignmentRegion != null)
            {
                bool assigned = entry.allowedRegionIds?.Contains(assignmentRegion.regionId) == true;
                var assign = new Button(ToggleAssignment) { text = assigned ? "Remove From Region" : "Add To Region" };
                assign.SetEnabled(catalog.regionCatalog == assignmentCatalog && !string.IsNullOrWhiteSpace(entry.id));
                details.Add(assign);
                if (catalog.regionCatalog != assignmentCatalog)
                    AddLabel(details, "Choose " + assignmentCatalog?.name + " In The Geographic Catalogue Field Above.", "muted");
            }

            if (entry.category != "cards")
            {
                AddDivider(details);
                AddLabel(details, "Requires", "section-title");
                AddRelated(entry.requiredIds ?? new List<string>(), true);
                var add = new Button(() =>
                {
                    requirementFor = requirementFor == selectedIndex ? -1 : selectedIndex;
                    relatedTo = category = search = ""; SyncSearch(); QueueRefresh();
                }) { text = "Choose Required Content" };
                add.AddToClassList("require-content-button");
                add.EnableInClassList("is-selected", requirementFor == selectedIndex);
                add.SetEnabled(!string.IsNullOrWhiteSpace(entry.id)); details.Add(add);
            }
            AddDivider(details);
            AddLabel(details, "Used By", "section-title");
            AddRelated((Entries ?? new List<ContentCatalog.Entry>()).Where(item => item?.requiredIds?.Contains(entry.id) == true).Select(item => item.id), false);
            AddDivider(details);
            BuildRegionSection(entry);
            AddDivider(details);
            details.Add(new Button(RemoveSelected) { text = "Remove Entry" });
        }

        void AddRelated(IEnumerable<string> ids, bool removable)
        {
            var items = ids.Distinct().Select(id => (id, entry: Find(id))).ToList();
            if (items.Count == 0) { AddLabel(details, "None", "muted"); return; }
            foreach (var group in items.GroupBy(item => item.entry?.category ?? "Missing Content"))
            {
                var foldout = new Foldout { text = CategoryName(group.Key) + " (" + group.Count() + ")", value = true };
                foldout.AddToClassList("related-group"); details.Add(foldout);
                foreach (var item in group)
                {
                    var row = new VisualElement(); row.AddToClassList("related-row");
                    if (item.entry != null) row.Add(Thumbnail(item.entry, "related-image"));
                    var button = new Button(() =>
                    { requirementFor = -1; selectedIndex = Entries.IndexOf(item.entry); QueueRefresh(); })
                    { text = item.entry != null ? Presentation(item.entry).title : item.id, tooltip = item.id };
                    button.AddToClassList("related-label"); button.SetEnabled(item.entry != null); row.Add(button);
                    if (removable)
                    {
                        var remove = new Button(() => Edit("Remove Required Content", entry => entry.requiredIds.RemoveAll(id => id == item.id)))
                        { text = "X", tooltip = "Remove Requirement" };
                        remove.AddToClassList("relation-remove"); row.Add(remove);
                    }
                    foldout.Add(row);
                }
            }
        }

        void BuildRegionSection(ContentCatalog.Entry entry)
        {
            if (catalog == null || entry == null) return;
            var section = new Foldout { text = "Regions", value = regionSectionOpen };
            section.AddToClassList("region-rules");
            section.RegisterValueChangedCallback(evt => { if (evt.target == section) regionSectionOpen = evt.newValue; });
            details.Add(section);
            var toggle = new Toggle("Requires Region") { value = entry.requiresRegion };
            toggle.RegisterValueChangedCallback(evt => Edit("Change Region Rules", item => item.requiresRegion = evt.newValue));
            section.Add(toggle);
            if (!entry.requiresRegion) return;
            if (catalog.regionCatalog == null)
            { AddLabel(section, "Choose A Geographic Catalogue Above To Configure Region Codes.", "muted"); return; }
            BuildRegionMap(section);
            AddRegionCodes(section, "Allowed Region IDs", entry.allowedRegionIds);
            AddRegionCodes(section, "Excluded Region IDs", entry.excludedRegionIds);
            if ((entry.allowedRegionIds?.Count ?? 0) == 0)
                AddLabel(section, (entry.excludedRegionIds?.Count ?? 0) == 0
                    ? "No Rules Yet: Restricted Until Configured."
                    : "Exclusions Only: Other Matched Regions Are Allowed.", "policy-note");
            else AddLabel(section, "Only Allowed Regions And Their Descendants Are Accepted. The Nearest Explicit Rule Overrides Inheritance.", "policy-note");
        }

        void BuildRegionMap(VisualElement section)
        {
            var levels = RegionLevels.Ordered.ToList();
            if (!levels.Contains(regionMapLevel)) regionMapLevel = RegionLevel.Country;
            string Name(RegionLevel level) => ObjectNames.NicifyVariableName(level.ToString());
            var levelField = new PopupField<RegionLevel>("Map Level", levels, regionMapLevel, Name, Name);
            levelField.AddToClassList("category-field");
            levelField.RegisterValueChangedCallback(evt =>
            {
                regionMapLevel = evt.newValue;
                fitPolicyMap = true;
                FitPolicyMap();
                policyMap?.MarkDirtyRepaint();
            });
            section.Add(levelField);
            if (policyMap == null)
            {
                policyMap = new RegionMap { Centre = regionMapCentre, Zoom = regionMapZoom, ShowTestPoint = false };
                policyMap.AddToClassList("content-region-map");
                policyMap.RegionVisible = region => region.level == regionMapLevel;
                policyMap.RegionColor = region =>
                {
                    int state = catalog.GetRegionRule(Selected, region);
                    return state > 0 ? new Color(.2f, .75f, .4f) : state < 0 ? new Color(.9f, .3f, .3f) : new Color(.55f, .58f, .62f);
                };
                policyMap.PointerClicked = ChooseMapRegion;
                policyMap.RegisterCallback<GeometryChangedEvent>(_ => FitPolicyMap());
            }
            if (mapCatalog != catalog.regionCatalog)
            {
                mapCatalog = catalog.regionCatalog;
                policyMap.InvalidateGeometry();
                fitPolicyMap = true;
            }
            policyMap.Catalog = catalog.regionCatalog;
            if (!regions.Any(link => link.region == previewRegion)) previewRegion = null;
            policyMap.Selected = previewRegion;
            section.Add(policyMap);
            var footer = new VisualElement(); footer.AddToClassList("map-footer"); section.Add(footer);
            var zoom = AddLabel(footer, "Zoom " + policyMap.Zoom, "map-zoom");
            policyMap.Status = message => { zoom.text = "Zoom " + policyMap.Zoom; zoom.tooltip = message; };
            var legend = new VisualElement(); legend.AddToClassList("region-legend"); footer.Add(legend);
            AddLabel(legend, "Allowed: Double Left Click", "allowed-state");
            AddLabel(legend, "Excluded: Double Right Click", "excluded-state");
            AddMapPreview(section);
            var controls = new VisualElement(); controls.AddToClassList("detail-actions"); section.Add(controls);
            var frame = new Button(() =>
            {
                if (previewRegion == null) return;
                fitPolicyMap = false;
                policyMap?.Fit(previewRegion);
            }) { text = "Frame Region" };
            frame.SetEnabled(previewRegion != null);
            controls.Add(frame);
            controls.Add(new Button(() => { fitPolicyMap = true; FitPolicyMap(); }) { text = "Frame Level" });
            controls.Add(new Button(() => policyMap?.Reload()) { text = "Refresh Map" });
            policyMap.Q<Label>(className: "map-attribution")?.RemoveFromHierarchy();
            var attribution = new Label("© OpenStreetMap") { tooltip = "OpenStreetMap Contributors; View Attribution" };
            attribution.AddToClassList("map-attribution");
            attribution.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            attribution.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());
            attribution.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button == 0) Application.OpenURL("https://www.openstreetmap.org/copyright");
                evt.StopPropagation();
            });
            policyMap.Add(attribution);
            policyMap.MarkDirtyRepaint();
            policyMap.schedule.Execute(() => { FitPolicyMap(); policyMap?.Reload(); }).StartingIn(20);
        }

        void FitPolicyMap()
        {
            if (!fitPolicyMap || policyMap == null || !(policyMap.contentRect.width > 0) || !(policyMap.contentRect.height > 0)) return;
            fitPolicyMap = false;
            var targets = regions.Where(link => link.region.level == regionMapLevel).Select(link => link.region).ToList();
            if (targets.Count > 0) policyMap.FitRegions(targets);
            else policyMap.Reload();
        }

        void DisposePolicyMap()
        {
            if (policyMap == null) return;
            regionMapCentre = policyMap.Centre;
            regionMapZoom = policyMap.Zoom;
            policyMap.Dispose();
            policyMap = null;
        }

        static bool HasAncestor(ContentRegion region, ContentRegion ancestor)
        {
            var seen = new HashSet<ContentRegion>();
            for (var item = region; item != null && seen.Add(item); item = item.parent)
                if (item == ancestor) return true;
            return false;
        }

        void ChooseMapRegion(GeoPoint point, int button, int clicks)
        {
            if (Selected == null || (button != 0 && button != 1)) return;
            var hits = regions.Select(link => link.region).Where(region => region.level == regionMapLevel
                && (region.Contains(point) || (region.boundary == RegionBoundary.DescendantsOnly
                    && regions.Any(child => HasAncestor(child.region, region) && child.region.Contains(point))))).ToList();
            if (hits.Count == 1) ChangeMapRegion(hits[0], button, clicks);
            else if (hits.Count > 1 && clicks < 2)
                ChangeMapRegion(hits.Contains(previewRegion) ? previewRegion : hits[0], button, clicks);
            else if (hits.Count > 1)
            {
                // Overlapping custom zones need an explicit choice instead of an arbitrary winner.
                var menu = new GenericMenu();
                foreach (var region in hits)
                    menu.AddItem(new GUIContent(region.displayName + " (" + region.regionId + ")"), false, () => ChangeMapRegion(region, button, clicks));
                menu.ShowAsContext();
            }
            else
            {
                previewRegion = null;
                if (policyMap != null) policyMap.Selected = null;
                QueueRefresh();
            }
        }

        void ChangeMapRegion(ContentRegion region, int button, int clicks)
        {
            previewRegion = region;
            if (policyMap != null) policyMap.Selected = region;
            if (clicks >= 2) Edit("Change Content Region State", entry => ToggleRegionState(entry, region.regionId, button));
            else QueueRefresh();
        }

        static void ToggleRegionState(ContentCatalog.Entry entry, string code, int button)
        {
            entry.allowedRegionIds ??= new List<string>();
            entry.excludedRegionIds ??= new List<string>();
            bool clear = button == 0 ? entry.allowedRegionIds.Contains(code) : entry.excludedRegionIds.Contains(code);
            entry.allowedRegionIds.RemoveAll(id => id == code);
            entry.excludedRegionIds.RemoveAll(id => id == code);
            if (!clear) (button == 0 ? entry.allowedRegionIds : entry.excludedRegionIds).Add(code);
        }

        void AddRegionCodes(VisualElement section, string title, List<string> codes)
        {
            bool allowed = title == "Allowed Region IDs";
            var foldout = new Foldout { text = title + " (" + (codes?.Count ?? 0) + ")",
                value = allowed ? allowedCodesOpen : excludedCodesOpen };
            foldout.AddToClassList("region-code-list");
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target != foldout) return;
                if (allowed) allowedCodesOpen = evt.newValue;
                else excludedCodesOpen = evt.newValue;
            });
            section.Add(foldout);
            if (codes == null || codes.Count == 0) { AddLabel(foldout, "None", "muted"); return; }
            foreach (string code in codes.Distinct())
            {
                var region = regions.FirstOrDefault(link => link.region.regionId == code)?.region;
                var row = new VisualElement(); row.AddToClassList("related-row");
                var frame = new Button(() =>
                {
                    regionMapLevel = region.level;
                    previewRegion = region;
                    fitPolicyMap = false;
                    if (policyMap != null) policyMap.Selected = region;
                    policyMap?.Fit(region);
                    QueueRefresh();
                }) { text = code, tooltip = region != null ? region.displayName : "Unknown Region Code" };
                frame.AddToClassList("related-label"); frame.SetEnabled(region != null); row.Add(frame);
                row.Add(new Button(() => Edit("Clear Region State", entry =>
                {
                    entry.allowedRegionIds?.RemoveAll(id => id == code);
                    entry.excludedRegionIds?.RemoveAll(id => id == code);
                })) { text = "X", tooltip = "Remove Explicit Rule" });
                foldout.Add(row);
            }
        }

        void AddMapPreview(VisualElement section)
        {
            var row = new VisualElement(); row.AddToClassList("region-preview"); section.Add(row);
            AddLabel(row, "Preview:", "preview-caption");
            regionPreviewValue = AddLabel(row, "", "preview-value");
            if (previewRegion == null)
            {
                regionPreviewValue.text = "Select A Region On The Map";
                return;
            }
            var matched = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<ContentRegion>();
            for (var region = previewRegion; region != null && seen.Add(region); region = region.parent) matched.Add(region.regionId);
            string reason = catalog.GetUnavailableReason(Selected.id, catalog.regionCatalog, matched);
            regionPreviewValue.text = previewRegion.displayName;
            regionPreviewValue.tooltip = reason ?? "";
            regionPreviewValue.AddToClassList(reason == null ? "allowed-state" : "excluded-state");
        }

        void ToggleAssignment()
        {
            if (assignmentRegion == null || catalog.regionCatalog != assignmentCatalog) return;
            Edit("Change Allowed Region", entry =>
            {
                entry.allowedRegionIds ??= new List<string>();
                if (!entry.allowedRegionIds.Remove(assignmentRegion.regionId)) entry.allowedRegionIds.Add(assignmentRegion.regionId);
                entry.requiresRegion = true;
            });
        }

        void ShowAddMenu()
        {
            if (catalog == null) return;
            var menu = new GenericMenu();
            foreach (string group in DefaultCategories.Concat((Entries ?? new List<ContentCatalog.Entry>()).Where(entry => entry != null).Select(entry => entry.category)).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct())
                menu.AddItem(new GUIContent(CategoryName(group)), false, () => AddEntry(group));
            menu.ShowAsContext();
        }

        void AddEntry(string group)
        {
            Undo.RecordObject(catalog, "Add Content Entry");
            catalog.entries ??= new List<ContentCatalog.Entry>();
            catalog.entries.Add(new ContentCatalog.Entry { category = group });
            selectedIndex = catalog.entries.Count - 1;
            requirementFor = -1; category = group; relatedTo = search = "";
            SyncSearch(); EditorUtility.SetDirty(catalog); QueueRefresh();
        }

        void RemoveSelected()
        {
            if (Selected == null || !EditorUtility.DisplayDialog("Remove Content Entry", "Remove '" + Selected.id
                + "' from this catalogue? Assets are kept. References in regions and requirements are kept for review.", "Remove", "Cancel")) return;
            Undo.RecordObject(catalog, "Remove Content Entry");
            catalog.entries.RemoveAt(selectedIndex);
            selectedIndex = requirementFor = -1; relatedTo = "";
            EditorUtility.SetDirty(catalog); QueueRefresh();
        }

        void StartPreviews()
        {
            // Unity builds previews asynchronously. Poll briefly, then stop instead of repainting forever.
            int attempts = 0;
            if (previews.Count == 0) return;
            previewTask = rootVisualElement.schedule.Execute(() =>
            {
                bool pending = false;
                foreach (var item in previews)
                {
                    if (item.asset == null) continue;
                    var texture = AssetPreview.GetAssetPreview(item.asset);
                    if (texture != null) item.image.image = texture;
                    else pending |= AssetPreview.IsLoadingAssetPreview(item.asset.GetEntityId());
                }
                if (!pending || ++attempts >= 30) previewTask?.Pause();
            }).Every(200);
        }
    }
}

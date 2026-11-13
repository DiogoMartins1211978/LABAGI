using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using static Tools.Georeferencing.Editor.MapProjection;

namespace Tools.Georeferencing.Editor
{
    public sealed class RegionMap : VisualElement, IDisposable
    {
        public GeoPoint Centre = new GeoPoint(41.178, -8.608);
        public int Zoom = 12;
        public RegionCatalog Catalog;
        public ContentRegion Selected;
        public GeoPoint TestPoint = new GeoPoint(41.178, -8.608);
        public readonly List<GeoPoint> Draft = new List<GeoPoint>();
        public bool DrawPolygon;
        public Action<GeoPoint, int> Clicked;
        public Action<string> Status;

        readonly Dictionary<(int x, int y, int z), Texture2D> tiles = new Dictionary<(int, int, int), Texture2D>();
        readonly Queue<(int x, int y, int z)> queue = new Queue<(int, int, int)>();
        HashSet<(int x, int y, int z)> required = new HashSet<(int, int, int)>();
        readonly HashSet<(int x, int y, int z)> drawn = new HashSet<(int, int, int)>();
        readonly MapPolygonCache polygons = new MapPolygonCache();
        readonly Dictionary<ContentRegion, GeoPolygon> circles = new Dictionary<ContentRegion, GeoPolygon>();
        MapBounds drawingBounds;
        (double x, double y) drawingCentre;
        double drawingScale;
        UnityWebRequest request;
        (int x, int y, int z) requestTile;
        Vector2 pointerDown, lastPointer;
        Vector2 lastClickPosition;
        double lastClickTime = -1;
        bool dragging, moved, failed;
        IVisualElementScheduledItem pendingLoad;

        public RegionMap()
        {
            AddToClassList("region-map");
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => ScheduleLoad());
            RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                pointerDown = lastPointer = evt.localPosition;
                dragging = true;
                moved = false;
                this.CapturePointer(evt.pointerId);
            });
            RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging) return;
                Vector2 position = evt.localPosition;
                if (Vector2.Distance(pointerDown, position) > 4) moved = true;
                if (moved)
                {
                    var world = Project(Centre, Zoom);
                    Centre = Unproject(world.x - position.x + lastPointer.x, world.y - position.y + lastPointer.y, Zoom);
                    MarkDirtyRepaint();
                    ScheduleLoad();
                }
                lastPointer = position;
            });
            RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button != 0 || !dragging) return;
                dragging = false;
                this.ReleasePointer(evt.pointerId);
                if (moved || !OnWorld(evt.localPosition)) { lastClickTime = -1; return; }
                var point = FromScreen(evt.localPosition);
                if (DrawPolygon) { Draft.Add(point); lastClickTime = -1; }
                else
                {
                    // Count completed clicks locally; pointer capture can reset native click counts.
                    double now = EditorApplication.timeSinceStartup;
                    Vector2 position = evt.localPosition;
                    int clicks = now - lastClickTime <= .5 && Vector2.Distance(position, lastClickPosition) <= 5 ? 2 : 1;
                    lastClickTime = clicks == 2 ? -1 : now;
                    lastClickPosition = position;
                    TestPoint = point;
                    Clicked?.Invoke(point, clicks);
                }
                MarkDirtyRepaint();
            });
            RegisterCallback<WheelEvent>(evt =>
            {
                lastClickTime = -1;
                // Preserve the coordinate under the cursor while zooming.
                Vector2 cursor = OnWorld(evt.localMousePosition) ? evt.localMousePosition : contentRect.center;
                var anchor = FromScreen(cursor);
                Zoom = Mathf.Clamp(Zoom + (evt.delta.y < 0 ? 1 : -1), 0, 18);
                var projected = Project(anchor, Zoom);
                Centre = Unproject(projected.x - cursor.x + contentRect.width / 2,
                    projected.y - cursor.y + contentRect.height / 2, Zoom);
                evt.StopPropagation();
                Reload();
            });
            EditorApplication.update += Poll;
        }

        public void Reload()
        {
            ConstrainView();
            MarkDirtyRepaint();
            ScheduleLoad();
        }

        void ScheduleLoad()
        {
            // Debounce movement so only the settled viewport is requested.
            ConstrainView();
            pendingLoad?.Pause();
            pendingLoad = schedule.Execute(LoadVisibleTiles).StartingIn(250);
        }

        void ConstrainView()
        {
            if (contentRect.width <= 0 || contentRect.height <= 0) return;
            if (!Centre.IsValid) Centre = new GeoPoint(0, 0);
            Centre = Constrain(Centre, Zoom, contentRect.width, contentRect.height);
        }

        GeoPoint FromScreen(Vector2 point)
        {
            var centre = Project(Centre, Zoom);
            return Unproject(centre.x + point.x - contentRect.width / 2,
                centre.y + point.y - contentRect.height / 2, Zoom);
        }

        bool OnWorld(Vector2 point)
        {
            var centre = Project(Centre, Zoom);
            double x = centre.x + point.x - contentRect.width / 2;
            double y = centre.y + point.y - contentRect.height / 2;
            double size = 256 * Math.Pow(2, Zoom);
            return x >= 0 && x <= size && y >= 0 && y <= size;
        }

        public void InvalidateGeometry()
        {
            polygons.Clear();
            circles.Clear();
            MarkDirtyRepaint();
        }

        Vector2 Screen(GeoPoint point) => ScreenProjected(Project(point, 0));

        Vector2 ScreenProjected((double x, double y) point)
        {
            return new Vector2((float)(point.x * drawingScale - drawingCentre.x) + contentRect.width / 2,
                (float)(point.y * drawingScale - drawingCentre.y) + contentRect.height / 2);
        }

        public void Fit(ContentRegion region)
        {
            if (region == null) return;
            var points = BoundaryPoints(region).ToList();
            if (points.Count == 0 && Catalog != null)
                foreach (var child in Catalog.regions.Where(item => IsDescendant(item, region)))
                    points.AddRange(BoundaryPoints(child));
            FitPoints(points);
        }

        public void FitCatalog()
        {
            if (Catalog == null) return;
            FitPoints(Catalog.regions.Where(region => region != null).SelectMany(BoundaryPoints).ToList());
        }

        void FitPoints(List<GeoPoint> points)
        {
            points = points.Where(point => point.IsValid).ToList();
            if (points.Count == 0) { Reload(); return; }
            var first = Project(points[0], 0);
            double minX = first.x, maxX = first.x, minY = first.y, maxY = first.y;
            foreach (var point in points)
            {
                var projected = Project(point, 0);
                double x = projected.x;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, projected.y); maxY = Math.Max(maxY, projected.y);
            }
            double scale = Math.Min(Math.Max(100, contentRect.width - 70) / Math.Max(.00001, maxX - minX),
                Math.Max(100, contentRect.height - 70) / Math.Max(.00001, maxY - minY));
            Zoom = Mathf.Clamp((int)Math.Floor(Math.Log(scale, 2)), 0, 18);
            Centre = Unproject((minX + maxX) / 2, (minY + maxY) / 2, 0);
            Reload();
        }

        static bool IsDescendant(ContentRegion child, ContentRegion ancestor)
        {
            var seen = new HashSet<ContentRegion>();
            for (var current = child; current != null && seen.Add(current); current = current.parent)
                if (current == ancestor) return true;
            return false;
        }

        static IEnumerable<GeoPoint> BoundaryPoints(ContentRegion region)
        {
            if (region.boundary == RegionBoundary.Circle && region.centre.IsValid
                && GeoGeometry.Finite(region.radiusKilometres) && region.radiusKilometres > 0)
            {
                double angle = region.radiusKilometres / 6371.0088;
                double lat = region.centre.latitude * Math.PI / 180;
                double lon = region.centre.longitude * Math.PI / 180;
                for (int i = 0; i < 120; i++)
                {
                    double bearing = i * Math.PI / 60;
                    double y = Math.Asin(Math.Sin(lat) * Math.Cos(angle) + Math.Cos(lat) * Math.Sin(angle) * Math.Cos(bearing));
                    double x = lon + Math.Atan2(Math.Sin(bearing) * Math.Sin(angle) * Math.Cos(lat), Math.Cos(angle) - Math.Sin(lat) * Math.Sin(y));
                    yield return new GeoPoint(y * 180 / Math.PI, GeoGeometry.WrapLongitude(x * 180 / Math.PI));
                }
            }
            else if (region.boundary == RegionBoundary.Polygon && region.polygons != null)
                foreach (var polygon in region.polygons)
                    if (polygon?.outline?.points != null)
                        foreach (var point in polygon.outline.points) yield return point;
        }

        public void LoadVisibleTiles()
        {
            if (contentRect.width < 1 || contentRect.height < 1) return;
            // Replace obsolete queued views; an in-flight tile can finish into the cache.
            queue.Clear();
            failed = false;
            ConstrainView();
            required = VisibleTiles(Centre, Zoom, contentRect.width, contentRect.height);
            foreach (var tile in required)
                if (!tiles.ContainsKey(tile) && (request == null || tile != requestTile)) queue.Enqueue(tile);
            ReportProgress();
        }

        static string CachePath((int x, int y, int z) tile) => Path.Combine("Library", "GeoreferencingMap",
            tile.z + "_" + tile.x + "_" + tile.y + ".png");

        void ReportProgress()
        {
            int ready = required.Count(tiles.ContainsKey);
            Status?.Invoke(ready == required.Count ? "Map Ready: Zoom " + Zoom
                : "Loading Map View: " + ready + "/" + required.Count + " Tiles");
            MarkDirtyRepaint();
        }

        void Poll()
        {
            if (request != null)
            {
                if (!request.isDone) return;
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        byte[] data = request.downloadHandler.data;
                        Directory.CreateDirectory(Path.GetDirectoryName(CachePath(requestTile)));
                        File.WriteAllBytes(CachePath(requestTile), data);
                        AddTexture(requestTile, data);
                    }
                    catch (Exception error) { failed = true; Status?.Invoke(error.Message); }
                }
                else if (required.Contains(requestTile))
                {
                    failed = true;
                    Status?.Invoke("Map Unavailable: " + request.error);
                    queue.Clear();
                }
                request.Dispose(); request = null;
                if (!failed) ReportProgress();
                MarkDirtyRepaint();
            }
            if (queue.Count == 0) return;
            var tile = queue.Dequeue();
            if (tiles.ContainsKey(tile)) return;
            string path = CachePath(tile);
            try
            {
                if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromDays(7))
                {
                    AddTexture(tile, File.ReadAllBytes(path));
                    if (!failed) ReportProgress();
                    MarkDirtyRepaint();
                    return;
                }
            }
            catch (IOException error) { Status?.Invoke(error.Message); }
            requestTile = tile;
            request = UnityWebRequest.Get($"https://tile.openstreetmap.org/{tile.z}/{tile.x}/{tile.y}.png");
            request.SetRequestHeader("User-Agent", "ContentRegionEditor/1.0 (Unity Editor; interactive authoring)");
            request.timeout = 15;
            request.SendWebRequest();
        }

        void AddTexture((int x, int y, int z) key, byte[] data)
        {
            var texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            if (!texture.LoadImage(data))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw new IOException("The Map Server Returned An Invalid Tile Image");
            }
            texture.wrapMode = TextureWrapMode.Clamp;
            if (tiles.TryGetValue(key, out var previous)) UnityEngine.Object.DestroyImmediate(previous);
            tiles[key] = texture;
            // Bound memory while leaving the persistent seven-day cache untouched.
            if (tiles.Count > 192)
            {
                foreach (var candidate in tiles.Keys.ToArray())
                    if (candidate != key && !required.Contains(candidate) && !drawn.Contains(candidate))
                    {
                        UnityEngine.Object.DestroyImmediate(tiles[candidate]);
                        tiles.Remove(candidate);
                        break;
                    }
            }
        }

        void Draw(MeshGenerationContext context)
        {
            var centre = Project(Centre, Zoom);
            drawingCentre = centre;
            drawingScale = Math.Pow(2, Zoom);
            drawingBounds = new MapBounds(
                Math.Max(0, (centre.x - contentRect.width / 2 - 2) / drawingScale),
                Math.Max(0, (centre.y - contentRect.height / 2 - 2) / drawingScale),
                Math.Min(256, (centre.x + contentRect.width / 2 + 2) / drawingScale),
                Math.Min(256, (centre.y + contentRect.height / 2 + 2) / drawingScale));
            drawn.Clear();
            foreach (var tile in VisibleTiles(Centre, Zoom, contentRect.width, contentRect.height))
            {
                var rect = new Rect((float)(tile.x * 256 - centre.x) + contentRect.width / 2,
                    (float)(tile.y * 256 - centre.y) + contentRect.height / 2, 256, 256);
                DrawCell(context, tile, rect, 0);
            }

            var painter = context.painter2D;
            if (Catalog != null)
            {
                var regions = Catalog.regions.Where(item => item != null).Distinct()
                    .OrderBy(item => RegionLevels.Rank(item.level)).ToList();
                // Draw each region once, with selected descendants above the other boundaries.
                var highlighted = new HashSet<ContentRegion>(regions.Where(region => region == Selected
                    || (Selected != null && Selected.boundary == RegionBoundary.DescendantsOnly
                        && IsDescendant(region, Selected))));
                foreach (var region in regions)
                    if (!highlighted.Contains(region)) DrawRegion(painter, region, false);
                foreach (var region in regions)
                    if (region != Selected && highlighted.Contains(region)) DrawRegion(painter, region, true);
            }
            if (Selected != null) DrawRegion(painter, Selected, true);
            painter.lineWidth = 2;
            painter.strokeColor = new Color(1, .65f, .2f);
            painter.BeginPath();
            for (int index = 0; index < Draft.Count; index++)
                if (index == 0) painter.MoveTo(Screen(Draft[index]));
                else painter.LineTo(Screen(Draft[index]));
            painter.Stroke();
            Vector2 test = Screen(TestPoint);
            painter.BeginPath();
            painter.MoveTo(test + Vector2.left * 7); painter.LineTo(test + Vector2.right * 7);
            painter.MoveTo(test + Vector2.up * 7); painter.LineTo(test + Vector2.down * 7);
            painter.Stroke();
        }

        void DrawCell(MeshGenerationContext context, (int x, int y, int z) cell, Rect rect, int depth)
        {
            // Choose one texture per patch. A ready tile replaces its fallback; they never stack.
            for (int level = cell.z; level >= 0; level--)
            {
                int factor = 1 << (cell.z - level);
                var source = (cell.x / factor, cell.y / factor, level);
                if (!tiles.TryGetValue(source, out var texture)) continue;
                var uv = TileUV(cell, level);
                DrawTile(context, rect, texture, new Rect(uv.u, uv.v, uv.size, uv.size));
                drawn.Add(source);
                return;
            }

            // On zoom-out, reuse cached children in four disjoint patches while loading.
            if (depth >= 2 || cell.z >= 18) return;
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    DrawCell(context, (cell.x * 2 + x, cell.y * 2 + y, cell.z + 1),
                        new Rect(rect.x + x * rect.width / 2, rect.y + y * rect.height / 2,
                            rect.width / 2, rect.height / 2), depth + 1);
        }

        void DrawRegion(Painter2D painter, ContentRegion region, bool selected)
        {
            Color color = RegionPalette.DisplayColor(region);
            painter.fillColor = new Color(color.r, color.g, color.b, selected ? .28f : .12f);
            painter.strokeColor = selected ? Color.white : new Color(color.r, color.g, color.b, .85f);
            painter.lineWidth = selected ? 3 : 1.5f;
            if (region.boundary == RegionBoundary.Circle)
            {
                if (!circles.TryGetValue(region, out var circle))
                {
                    circle = new GeoPolygon { outline = new GeoRing { points = BoundaryPoints(region).ToList() } };
                    circles.Add(region, circle);
                }
                DrawBoundaryPolygon(painter, circle);
            }
            else if (region.boundary == RegionBoundary.Polygon && region.polygons != null)
                foreach (var polygon in region.polygons) DrawBoundaryPolygon(painter, polygon);
        }

        void DrawBoundaryPolygon(Painter2D painter, GeoPolygon source)
        {
            var view = polygons.Get(source, drawingBounds, Zoom);
            if (view.FillRings.Count == 0) return;
            // Multipart regions are filled polygon by polygon, with only that polygon's holes.
            painter.BeginPath();
            foreach (var ring in view.FillRings)
            {
                painter.MoveTo(ScreenProjected((ring[0].X, ring[0].Y)));
                for (int index = 1; index < ring.Count; index++)
                    painter.LineTo(ScreenProjected((ring[index].X, ring[index].Y)));
                painter.ClosePath();
            }
            painter.Fill(FillRule.OddEven);
            painter.BeginPath();
            foreach (var segment in view.Border)
            {
                painter.MoveTo(ScreenProjected((segment.a.X, segment.a.Y)));
                painter.LineTo(ScreenProjected((segment.b.X, segment.b.Y)));
            }
            painter.Stroke();
        }

        static void DrawTile(MeshGenerationContext context, Rect rect, Texture2D texture, Rect uv)
        {
            var mesh = context.Allocate(4, 6, texture);
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMin, Vertex.nearZ), tint = Color.white, uv = new Vector2(uv.xMin, uv.yMax) });
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMin, Vertex.nearZ), tint = Color.white, uv = new Vector2(uv.xMax, uv.yMax) });
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, rect.yMax, Vertex.nearZ), tint = Color.white, uv = new Vector2(uv.xMax, uv.yMin) });
            mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, rect.yMax, Vertex.nearZ), tint = Color.white, uv = new Vector2(uv.xMin, uv.yMin) });
            foreach (ushort index in new ushort[] { 0, 1, 2, 2, 3, 0 }) mesh.SetNextIndex(index);
        }

        public void Dispose()
        {
            pendingLoad?.Pause();
            EditorApplication.update -= Poll;
            request?.Abort(); request?.Dispose(); request = null;
            foreach (var texture in tiles.Values) UnityEngine.Object.DestroyImmediate(texture);
            tiles.Clear(); queue.Clear();
            polygons.Clear();
            circles.Clear();
        }
    }
}


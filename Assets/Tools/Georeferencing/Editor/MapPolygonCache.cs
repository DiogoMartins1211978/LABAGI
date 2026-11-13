using System;
using System.Collections.Generic;

namespace Tools.Georeferencing.Editor
{
    public readonly struct MapPoint
    {
        public readonly double X, Y;
        public MapPoint(double x, double y) { X = x; Y = y; }
    }

    public readonly struct MapBounds : IEquatable<MapBounds>
    {
        public readonly double MinX, MinY, MaxX, MaxY;
        public MapBounds(double minX, double minY, double maxX, double maxY)
        { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; }

        public bool Intersects(MapBounds other) => MaxX >= other.MinX && MinX <= other.MaxX
            && MaxY >= other.MinY && MinY <= other.MaxY;
        public bool Equals(MapBounds other) => MinX == other.MinX && MinY == other.MinY
            && MaxX == other.MaxX && MaxY == other.MaxY;
        public override bool Equals(object value) => value is MapBounds other && Equals(other);
        public override int GetHashCode() => MinX.GetHashCode() ^ MinY.GetHashCode() ^ MaxX.GetHashCode() ^ MaxY.GetHashCode();
    }

    public sealed class MapPolygonView
    {
        public readonly List<List<MapPoint>> FillRings = new List<List<MapPoint>>();
        public readonly List<(MapPoint a, MapPoint b)> Border = new List<(MapPoint, MapPoint)>();
    }

    public sealed class MapPolygonCache
    {
        readonly Dictionary<GeoPolygon, Polygon> polygons = new Dictionary<GeoPolygon, Polygon>();
        static readonly MapPolygonView Empty = new MapPolygonView();

        public void Clear() => polygons.Clear();

        public MapPolygonView Get(GeoPolygon source, MapBounds viewport, int zoom)
        {
            if (source?.outline?.points == null) return Empty;
            if (!polygons.TryGetValue(source, out var polygon))
            {
                polygon = new Polygon(source);
                polygons.Add(source, polygon);
            }
            return polygon.View(viewport, zoom);
        }

        sealed class Polygon
        {
            readonly Ring outline;
            readonly List<Ring> holes = new List<Ring>();
            MapBounds lastBounds;
            int lastZoom;
            MapPolygonView lastView;

            public Polygon(GeoPolygon source)
            {
                outline = new Ring(source.outline);
                if (source.holes != null)
                    foreach (var hole in source.holes) holes.Add(new Ring(hole));
            }

            public MapPolygonView View(MapBounds bounds, int zoom)
            {
                // Tile arrivals and selection changes reuse the same clipped geometry.
                if (lastView != null && lastBounds.Equals(bounds) && lastZoom == zoom) return lastView;
                lastBounds = bounds;
                lastZoom = zoom;
                lastView = Empty;
                if (!outline.Bounds.Intersects(bounds)) return lastView;
                var visible = Clip(outline.Level(zoom), bounds);
                if (visible.Count < 3) return lastView;
                lastView = new MapPolygonView();
                lastView.FillRings.Add(visible);
                AddBorder(outline.Level(zoom), bounds, lastView);
                foreach (var hole in holes)
                {
                    if (!hole.Bounds.Intersects(bounds)) continue;
                    var points = hole.Level(zoom);
                    var clipped = Clip(points, bounds);
                    if (clipped.Count >= 3) lastView.FillRings.Add(clipped);
                    AddBorder(points, bounds, lastView);
                }
                return lastView;
            }
        }

        sealed class Ring
        {
            readonly List<MapPoint> points = new List<MapPoint>();
            readonly Dictionary<int, List<MapPoint>> levels = new Dictionary<int, List<MapPoint>>();
            public readonly MapBounds Bounds;

            public Ring(GeoRing source)
            {
                double minX = double.PositiveInfinity, minY = minX;
                double maxX = double.NegativeInfinity, maxY = maxX;
                if (source?.points != null)
                    foreach (var point in source.points)
                    {
                        if (!point.IsValid) { points.Clear(); break; }
                        var projected = MapProjection.Project(point, 0);
                        points.Add(new MapPoint(projected.x, projected.y));
                        minX = Math.Min(minX, projected.x); maxX = Math.Max(maxX, projected.x);
                        minY = Math.Min(minY, projected.y); maxY = Math.Max(maxY, projected.y);
                    }
                if (points.Count > 0 && !Same(points[0], points[points.Count - 1])) points.Add(points[0]);
                Bounds = new MapBounds(minX, minY, maxX, maxY);
            }

            public List<MapPoint> Level(int zoom)
            {
                if (levels.TryGetValue(zoom, out var result)) return result;
                // Sub-pixel display reduction never changes the source used for region matching.
                result = Simplify(points, .6 / Math.Pow(2, zoom));
                if (levels.Count >= 4) levels.Clear();
                levels.Add(zoom, result);
                return result;
            }
        }

        static bool Same(MapPoint a, MapPoint b) => a.X == b.X && a.Y == b.Y;

        static List<MapPoint> Simplify(List<MapPoint> source, double tolerance)
        {
            if (source.Count <= 4) return source;
            double squared = tolerance * tolerance;
            var points = new List<MapPoint> { source[0] };
            foreach (var point in source)
            {
                var previous = points[points.Count - 1];
                if (Squared(point.X - previous.X, point.Y - previous.Y) >= squared / 4) points.Add(point);
            }
            if (!Same(points[points.Count - 1], source[source.Count - 1])) points.Add(source[source.Count - 1]);
            var keep = new bool[points.Count];
            keep[0] = keep[points.Count - 1] = true;
            var stack = new Stack<(int start, int end)>();
            stack.Push((0, points.Count - 1));
            while (stack.Count > 0)
            {
                var segment = stack.Pop();
                var a = points[segment.start]; var b = points[segment.end];
                double dx = b.X - a.X, dy = b.Y - a.Y, length = Squared(dx, dy);
                int chosen = -1;
                double maximum = squared;
                for (int index = segment.start + 1; index < segment.end; index++)
                {
                    var point = points[index];
                    double ratio = length == 0 ? 0 : Math.Max(0, Math.Min(1, ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / length));
                    double distance = Squared(point.X - a.X - ratio * dx, point.Y - a.Y - ratio * dy);
                    if (distance > maximum) { maximum = distance; chosen = index; }
                }
                if (chosen < 0) continue;
                keep[chosen] = true;
                stack.Push((segment.start, chosen)); stack.Push((chosen, segment.end));
            }
            var result = new List<MapPoint>();
            for (int index = 0; index < points.Count; index++) if (keep[index]) result.Add(points[index]);
            return result;
        }

        static double Squared(double x, double y) => x * x + y * y;

        static List<MapPoint> Clip(List<MapPoint> points, MapBounds bounds)
        {
            // Clip in double-precision world coordinates before converting to screen floats.
            var result = ClipEdge(points, 0, bounds.MinX, true);
            result = ClipEdge(result, 0, bounds.MaxX, false);
            result = ClipEdge(result, 1, bounds.MinY, true);
            return ClipEdge(result, 1, bounds.MaxY, false);
        }

        static List<MapPoint> ClipEdge(List<MapPoint> input, int axis, double boundary, bool greater)
        {
            var result = new List<MapPoint>();
            if (input.Count == 0) return result;
            var previous = input[input.Count - 1];
            double previousValue = axis == 0 ? previous.X : previous.Y;
            bool previousInside = greater ? previousValue >= boundary : previousValue <= boundary;
            foreach (var current in input)
            {
                double value = axis == 0 ? current.X : current.Y;
                bool inside = greater ? value >= boundary : value <= boundary;
                if (inside != previousInside)
                {
                    double fraction = (boundary - previousValue) / (value - previousValue);
                    result.Add(new MapPoint(previous.X + (current.X - previous.X) * fraction,
                        previous.Y + (current.Y - previous.Y) * fraction));
                }
                if (inside) result.Add(current);
                previous = current; previousValue = value; previousInside = inside;
            }
            return result;
        }

        static void AddBorder(List<MapPoint> points, MapBounds bounds, MapPolygonView view)
        {
            // Only original boundary segments are stroked, not artificial viewport clipping edges.
            for (int index = 1; index < points.Count; index++)
            {
                var a = points[index - 1]; var b = points[index];
                double dx = b.X - a.X, dy = b.Y - a.Y, start = 0, end = 1;
                if (!Cut(-dx, a.X - bounds.MinX, ref start, ref end)
                    || !Cut(dx, bounds.MaxX - a.X, ref start, ref end)
                    || !Cut(-dy, a.Y - bounds.MinY, ref start, ref end)
                    || !Cut(dy, bounds.MaxY - a.Y, ref start, ref end)) continue;
                view.Border.Add((new MapPoint(a.X + start * dx, a.Y + start * dy),
                    new MapPoint(a.X + end * dx, a.Y + end * dy)));
            }
        }

        static bool Cut(double direction, double distance, ref double start, ref double end)
        {
            if (direction == 0) return distance >= 0;
            double fraction = distance / direction;
            if (direction < 0) { if (fraction > end) return false; start = Math.Max(start, fraction); }
            else { if (fraction < start) return false; end = Math.Min(end, fraction); }
            return true;
        }
    }
}

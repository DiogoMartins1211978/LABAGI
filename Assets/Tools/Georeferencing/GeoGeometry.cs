using System;
using System.Collections.Generic;

namespace Tools.Georeferencing
{
    [Serializable]
    public struct GeoPoint
    {
        public double latitude;
        public double longitude;

        public GeoPoint(double latitude, double longitude)
        {
            this.latitude = latitude;
            this.longitude = longitude;
        }

        public bool IsValid => GeoGeometry.Finite(latitude) && GeoGeometry.Finite(longitude)
            && latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180;
    }

    [Serializable]
    public sealed class GeoRing
    {
        public List<GeoPoint> points = new List<GeoPoint>();
    }

    [Serializable]
    public sealed class GeoPolygon
    {
        public GeoRing outline = new GeoRing();
        public List<GeoRing> holes = new List<GeoRing>();
    }

    public static class GeoGeometry
    {
        const double EarthRadius = 6371008.8;
        const double Radians = Math.PI / 180;

        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public static double WrapLongitude(double value) => ((value + 180) % 360 + 360) % 360 - 180;

        public static double DistanceMetres(GeoPoint a, GeoPoint b)
        {
            double lat = (b.latitude - a.latitude) * Radians;
            double lon = WrapLongitude(b.longitude - a.longitude) * Radians;
            double h = Math.Pow(Math.Sin(lat / 2), 2)
                + Math.Cos(a.latitude * Radians) * Math.Cos(b.latitude * Radians)
                * Math.Pow(Math.Sin(lon / 2), 2);
            return EarthRadius * 2 * Math.Asin(Math.Sqrt(Math.Min(1, Math.Max(0, h))));
        }

        public static bool ValidRing(GeoRing ring)
        {
            if (ring?.points == null || ring.points.Count < 3) return false;
            var first = ring.points[0];
            var second = first;
            bool hasSecond = false, hasThird = false;
            foreach (var point in ring.points)
            {
                if (!point.IsValid) return false;
                if (point.latitude == first.latitude && point.longitude == first.longitude) continue;
                if (!hasSecond) { second = point; hasSecond = true; }
                else if (point.latitude != second.latitude || point.longitude != second.longitude) hasThird = true;
            }
            if (!hasThird) return false;
            double area = 0;
            double x = ring.points[0].longitude;
            double firstX = x;
            for (int i = 0; i < ring.points.Count; i++)
            {
                var a = ring.points[i];
                var b = ring.points[(i + 1) % ring.points.Count];
                double next = i == ring.points.Count - 1 ? firstX : x + WrapLongitude(b.longitude - a.longitude);
                area += x * b.latitude - next * a.latitude;
                x = next;
            }
            return Math.Abs(area) > 1e-12;
        }

        public static bool Contains(GeoPolygon polygon, GeoPoint point)
        {
            if (polygon == null || polygon.holes == null || !point.IsValid) return false;
            if (!InsideRing(polygon.outline, point)) return false;
            foreach (var hole in polygon.holes)
                if (InsideRing(hole, point)) return false;
            return true;
        }

        // Unwrap consecutive longitudes so small polygons can cross the date line.
        // Rings describe straight edges in latitude/longitude, not geodesic arcs.
        static bool InsideRing(GeoRing ring, GeoPoint point)
        {
            if (!ValidRing(ring)) return false;
            var vertices = ring.points;
            double previousX = vertices[0].longitude;
            double queryX = previousX + WrapLongitude(point.longitude - previousX);
            bool inside = false;
            double firstX = previousX;
            for (int i = 0; i < vertices.Count; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % vertices.Count];
                double ax = previousX;
                double bx = i == vertices.Count - 1 ? firstX : ax + WrapLongitude(b.longitude - a.longitude);
                double cross = (queryX - ax) * (b.latitude - a.latitude)
                    - (point.latitude - a.latitude) * (bx - ax);

                // Include outer edges. Hole edges are excluded by Contains.
                if (Math.Abs(cross) < 1e-10 && queryX >= Math.Min(ax, bx) - 1e-10
                    && queryX <= Math.Max(ax, bx) + 1e-10
                    && point.latitude >= Math.Min(a.latitude, b.latitude) - 1e-10
                    && point.latitude <= Math.Max(a.latitude, b.latitude) + 1e-10) return true;

                if ((a.latitude > point.latitude) != (b.latitude > point.latitude)
                    && queryX < (bx - ax) * (point.latitude - a.latitude) / (b.latitude - a.latitude) + ax)
                    inside = !inside;
                previousX = bx;
            }
            return inside;
        }
    }
}

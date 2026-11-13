using System;
using System.Collections.Generic;

namespace Tools.Georeferencing.Editor
{
    // Tiles and overlays share the same Web Mercator projection and XYZ tile grid.
    public static class MapProjection
    {
        public static (double x, double y) Project(GeoPoint point, int zoom)
        {
            double size = 256 * Math.Pow(2, zoom);
            double sine = Math.Sin(Math.Max(-85.05112878, Math.Min(85.05112878, point.latitude)) * Math.PI / 180);
            return ((point.longitude + 180) / 360 * size,
                (0.5 - Math.Log((1 + sine) / (1 - sine)) / (4 * Math.PI)) * size);
        }

        public static GeoPoint Unproject(double x, double y, int zoom)
        {
            double size = 256 * Math.Pow(2, zoom);
            y = Math.Max(0, Math.Min(size, y));
            return new GeoPoint(Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / size))) * 180 / Math.PI,
                Math.Max(-180, Math.Min(180, x / size * 360 - 180)));
        }

        public static GeoPoint Constrain(GeoPoint point, int zoom, double width, double height)
        {
            double size = 256 * Math.Pow(2, zoom);
            var projected = Project(point, zoom);
            double x = width >= size ? size / 2 : Math.Max(width / 2, Math.Min(size - width / 2, projected.x));
            double y = height >= size ? size / 2 : Math.Max(height / 2, Math.Min(size - height / 2, projected.y));
            return Unproject(x, y, zoom);
        }

        public static HashSet<(int x, int y, int z)> VisibleTiles(GeoPoint point, int zoom, double width, double height)
        {
            var result = new HashSet<(int, int, int)>();
            var centre = Project(point, zoom);
            int count = 1 << zoom;
            int minX = Math.Max(0, (int)Math.Floor((centre.x - width / 2) / 256));
            int maxX = Math.Min(count - 1, (int)Math.Ceiling((centre.x + width / 2) / 256) - 1);
            int minY = Math.Max(0, (int)Math.Floor((centre.y - height / 2) / 256));
            int maxY = Math.Min(count - 1, (int)Math.Ceiling((centre.y + height / 2) / 256) - 1);
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    result.Add((x, y, zoom));
            return result;
        }

        public static (float u, float v, float size) TileUV((int x, int y, int z) cell, int sourceZoom)
        {
            int factor = 1 << (cell.z - sourceZoom);
            return ((float)(cell.x % factor) / factor,
                1f - (float)((cell.y % factor) + 1) / factor, 1f / factor);
        }
    }
}

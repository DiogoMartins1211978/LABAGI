using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Tools.Georeferencing.Editor
{
    public static class GeoJsonImporter
    {
        // GeoJSON uses longitude first, unlike GeoPoint's latitude-first constructor.
        public static List<GeoPolygon> Parse(string json)
        {
            var result = new List<GeoPolygon>();
            Read(JToken.Parse(json), result);
            if (result.Count == 0) throw new FormatException("The file must contain Polygon or MultiPolygon geometry.");
            return result;
        }

        static void Read(JToken token, List<GeoPolygon> result)
        {
            if (token == null || token.Type != JTokenType.Object)
                throw new FormatException("Expected a GeoJSON geometry or feature object.");
            switch ((string)token["type"])
            {
                case "FeatureCollection":
                    foreach (var feature in token["features"]) Read(feature, result);
                    break;
                case "Feature": Read(token["geometry"], result); break;
                case "Polygon": result.Add(Polygon(token["coordinates"])); break;
                case "MultiPolygon":
                    foreach (var polygon in token["coordinates"]) result.Add(Polygon(polygon));
                    break;
                default: throw new FormatException("Only polygon geometry is supported.");
            }
        }

        static GeoPolygon Polygon(JToken rings)
        {
            if (!(rings is JArray array) || array.Count == 0) throw new FormatException("Empty polygon.");
            var polygon = new GeoPolygon { outline = Ring(array[0]) };
            for (int i = 1; i < array.Count; i++) polygon.holes.Add(Ring(array[i]));
            return polygon;
        }

        static GeoRing Ring(JToken coordinates)
        {
            var ring = new GeoRing();
            foreach (var pair in coordinates)
            {
                if (!(pair is JArray array) || array.Count < 2) throw new FormatException("Invalid coordinate pair.");
                ring.points.Add(new GeoPoint((double)array[1], (double)array[0]));
            }
            if (!GeoGeometry.ValidRing(ring)) throw new FormatException("A ring needs three distinct valid points.");
            return ring;
        }
    }
}

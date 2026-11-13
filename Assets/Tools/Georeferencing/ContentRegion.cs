using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Tools.Georeferencing
{
    // Explicit values preserve existing assets when new levels are added.
    public enum RegionLevel { Continent = 0, Country = 1, AdministrativeArea = 4, Locality = 2, Subdivision = 5, CustomZone = 3 }

    public static class RegionLevels
    {
        static readonly RegionLevel[] Order = { RegionLevel.Continent, RegionLevel.Country,
            RegionLevel.AdministrativeArea, RegionLevel.Locality, RegionLevel.Subdivision, RegionLevel.CustomZone };

        public static IReadOnlyList<RegionLevel> Ordered => Order;

        public static int Rank(RegionLevel level) => System.Array.IndexOf(Order, level);

        public static RegionLevel Next(RegionLevel level) => Order[System.Math.Min(Order.Length - 1, Rank(level) + 1)];
    }
    public enum RegionBoundary { DescendantsOnly, Circle, Polygon }

    [CreateAssetMenu(menuName = "Georeferencing/Content Region")]
    public sealed class ContentRegion : ScriptableObject, ISerializationCallbackReceiver
    {
        // --- CONFIGURABLE FIELDS ---

        public string regionId;
        public string displayName;
        public RegionLevel level;
        public ContentRegion parent;
        public List<string> contentIds = new List<string>();
        // Preserve source identity when library assets are cloned or receive new game IDs.
        [HideInInspector] public string libraryRegionId;

        [Header("Appearance")]
        public Color regionColor;

        [Header("Boundary")]
        public RegionBoundary boundary = RegionBoundary.DescendantsOnly;
        public GeoPoint centre;
        public double radiusKilometres = 0.1;
        public List<GeoPolygon> polygons = new List<GeoPolygon>();

        [Header("Details")]
        [TextArea] public string description;
        [FormerlySerializedAs("source")]
        [Tooltip("Where the boundary data came from, including its provider and licence.")]
        [TextArea] public string dataSource;

        // Convert existing assets once, preserving their physical radius.
        [SerializeField, HideInInspector, FormerlySerializedAs("radiusMetres")]
        double legacyRadiusMetres = -1;

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            if (legacyRadiusMetres < 0) return;
            radiusKilometres = legacyRadiusMetres / 1000;
            legacyRadiusMetres = -1;
        }

        public bool Contains(GeoPoint point)
        {
            if (!point.IsValid) return false;
            if (boundary == RegionBoundary.Circle)
                return centre.IsValid && GeoGeometry.Finite(radiusKilometres) && radiusKilometres > 0
                    && GeoGeometry.DistanceMetres(centre, point) <= radiusKilometres * 1000;
            if (boundary == RegionBoundary.Polygon && polygons != null)
                foreach (var polygon in polygons)
                    if (GeoGeometry.Contains(polygon, point)) return true;
            return false;
        }
    }
}

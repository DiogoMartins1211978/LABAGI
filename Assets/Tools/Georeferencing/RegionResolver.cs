using System;
using System.Collections.Generic;

namespace Tools.Georeferencing
{
    public sealed class RegionResult
    {
        public readonly List<ContentRegion> Regions = new List<ContentRegion>();
        public readonly List<string> ContentIds = new List<string>();
        public readonly List<string> Errors = new List<string>();
        public bool IsValid => Errors.Count == 0;
    }

    public static class RegionResolver
    {
        public static RegionResult Resolve(RegionCatalog catalog, GeoPoint point)
        {
            var result = new RegionResult();
            if (!point.IsValid) result.Errors.Add("Latitude/longitude is invalid.");
            Validate(catalog, result.Errors);
            if (!result.IsValid) return result;

            var matched = new HashSet<ContentRegion>();
            foreach (var region in catalog.regions)
            {
                if (!region.Contains(point)) continue;
                // Parent regions contribute content even when they have no imported boundary.
                for (var ancestor = region; ancestor != null; ancestor = ancestor.parent)
                    matched.Add(ancestor);
            }

            var content = new HashSet<string>(StringComparer.Ordinal);
            foreach (var region in matched) result.Regions.Add(region);
            result.Regions.Sort((a, b) => string.CompareOrdinal(a.regionId, b.regionId));
            foreach (var region in result.Regions)
                foreach (string id in region.contentIds)
                    if (!string.IsNullOrWhiteSpace(id)) content.Add(id);
            result.ContentIds.AddRange(content);
            result.ContentIds.Sort(StringComparer.Ordinal);
            return result;
        }

        public static void Validate(RegionCatalog catalog, List<string> errors)
        {
            if (catalog == null || catalog.regions == null)
            {
                errors.Add("Assign a region catalog.");
                return;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var entries = new HashSet<ContentRegion>();
            foreach (var region in catalog.regions)
            {
                if (region == null) { errors.Add("Catalog contains an empty region."); continue; }
                if (!entries.Add(region)) errors.Add("Repeated region asset: " + region.name);
                if (string.IsNullOrWhiteSpace(region.regionId) || !ids.Add(region.regionId))
                    errors.Add("Missing or duplicate region ID: " + region.name);
                if (region.contentIds == null) errors.Add("Missing content list: " + region.name);
                if (RegionLevels.Rank(region.level) < 0) errors.Add("Unknown region level: " + region.name);
                if (region.boundary == RegionBoundary.Circle
                    && (!region.centre.IsValid || !GeoGeometry.Finite(region.radiusKilometres) || region.radiusKilometres <= 0))
                    errors.Add("Invalid circle: " + region.name);
                if (region.boundary == RegionBoundary.Polygon)
                {
                    if (region.polygons == null || region.polygons.Count == 0)
                        errors.Add("Add at least one polygon: " + region.name);
                    else foreach (var polygon in region.polygons)
                    {
                        if (polygon == null || !GeoGeometry.ValidRing(polygon.outline) || polygon.holes == null)
                            errors.Add("Invalid polygon: " + region.name);
                        else foreach (var hole in polygon.holes)
                            if (!GeoGeometry.ValidRing(hole)) errors.Add("Invalid polygon hole: " + region.name);
                    }
                }
            }

            foreach (var region in entries)
            {
                var chain = new HashSet<ContentRegion>();
                for (var ancestor = region; ancestor != null; ancestor = ancestor.parent)
                {
                    if (!chain.Add(ancestor)) { errors.Add("Parent cycle: " + region.name); break; }
                    if (!entries.Contains(ancestor)) { errors.Add("Parent is outside catalog: " + ancestor.name); break; }
                    if (ancestor.parent != null && RegionLevels.Rank(ancestor.parent.level) >= RegionLevels.Rank(ancestor.level))
                        errors.Add("Parent must be a broader level: " + ancestor.name);
                }
            }
        }
    }
}

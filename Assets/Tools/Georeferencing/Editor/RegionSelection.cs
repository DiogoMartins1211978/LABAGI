using System;
using System.Collections.Generic;
using System.Linq;

namespace Tools.Georeferencing.Editor
{
    public static class RegionSelection
    {
        public static bool IsFromLibrary(ContentRegion region)
        {
            if (region == null) return false;
            if (!string.IsNullOrEmpty(region.libraryRegionId)) return true;
            // Earlier imports used source IDs directly, before explicit provenance was stored.
            return region.regionId != null && (region.regionId.StartsWith("osm:relation:", StringComparison.Ordinal)
                || region.regionId.StartsWith("geo:continent:", StringComparison.Ordinal));
        }

        public static RegionResult Resolve(RegionCatalog catalog, ContentRegion selected)
        {
            var result = new RegionResult();
            RegionResolver.Validate(catalog, result.Errors);
            if (!result.IsValid || selected == null) return result;
            if (!catalog.regions.Contains(selected))
            {
                result.Errors.Add("Selected region is outside the catalogue.");
                return result;
            }
            for (var region = selected; region != null; region = region.parent) result.Regions.Add(region);
            result.Regions.Reverse();
            result.ContentIds.AddRange(result.Regions.SelectMany(region => region.contentIds)
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal));
            return result;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace Tools.Georeferencing.Editor
{
    static class RegionPalette
    {
        public static Color Next(RegionCatalog catalog)
        {
            var used = new HashSet<Color>();
            if (catalog != null)
                foreach (var region in catalog.regions)
                    if (region != null) used.Add(region.regionColor);
            for (int index = 0; ; index++)
            {
                Color candidate = Color.HSVToRGB((float)((index * .61803398875) % 1), .62f, .9f);
                if (!used.Contains(candidate)) return candidate;
            }
        }

        public static Color DisplayColor(ContentRegion region)
        {
            if (region.regionColor.a > 0) return region.regionColor;
            uint hash = 2166136261;
            foreach (char character in region.regionId ?? region.name) hash = (hash ^ character) * 16777619;
            return Color.HSVToRGB((hash % 360) / 360f, .62f, .9f);
        }
    }
}

using Tools.Content;
using UnityEngine;
using System.Linq;

namespace Tools.Georeferencing
{
    internal static class RegionContentBridge
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            StartupLocation.Resolved -= Publish;
            StartupLocation.Resolved += Publish;
            StartupLocation.Refreshing -= ContentAvailability.BeginRegionRefresh;
            StartupLocation.Refreshing += ContentAvailability.BeginRegionRefresh;
        }

        static void Publish(RegionResult result)
        {
            // Resolve the content catalogue's own geographic catalogue using the captured position.
            var catalog = ContentAvailability.RegionCatalog;
            var resolved = catalog == StartupLocation.Catalog ? result : StartupLocation.Resolve(catalog);
            ContentAvailability.SetRegions(catalog, resolved.IsValid
                ? resolved.Regions.Select(region => region.regionId) : null);
        }
    }
}

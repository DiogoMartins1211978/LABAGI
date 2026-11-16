using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tools.Georeferencing
{
    [CreateAssetMenu(menuName = "Georeferencing/Location Rules")]
    public sealed class LocationRules : ScriptableObject
    {
        [Serializable]
        public sealed class Rule
        {
            public LocationOrigin source;
            public bool enabled = true;
            [Tooltip("The most specific level permitted. Broader levels remain available.")]
            public RegionLevel minimumLevel = RegionLevel.AdministrativeArea;
            [Tooltip("Use a different level when the user grants precise location permission.")]
            public bool usePreciseLevel = true;
            public RegionLevel preciseMinimumLevel = RegionLevel.CustomZone;
        }

        [Tooltip("Use one entry per source. Missing or duplicate entries disable that source.")]
        public List<Rule> sources = new List<Rule>
        {
            new Rule { source = LocationOrigin.Unknown, enabled = false },
            new Rule { source = LocationOrigin.IPAddress, minimumLevel = RegionLevel.AdministrativeArea },
            new Rule { source = LocationOrigin.Cellular, minimumLevel = RegionLevel.AdministrativeArea },
            new Rule { source = LocationOrigin.WiFi, minimumLevel = RegionLevel.Locality },
            new Rule { source = LocationOrigin.Satellite, minimumLevel = RegionLevel.CustomZone },
            new Rule { source = LocationOrigin.Default, minimumLevel = RegionLevel.AdministrativeArea },
            new Rule { source = LocationOrigin.Obfuscated, minimumLevel = RegionLevel.AdministrativeArea },
            new Rule { source = LocationOrigin.DeviceLocation, minimumLevel = RegionLevel.AdministrativeArea,
                usePreciseLevel = true, preciseMinimumLevel = RegionLevel.CustomZone },
            new Rule { source = LocationOrigin.ConfigurationOverride, minimumLevel = RegionLevel.CustomZone },
            new Rule { source = LocationOrigin.Simulated, minimumLevel = RegionLevel.CustomZone }
        };

        [Tooltip("Additional level limit when only approximate location permission is available.")]
        public RegionLevel approximateMinimumLevel = RegionLevel.AdministrativeArea;

        public bool TryGetMinimumLevel(LocationOrigin source, LocationPrecision precision, out RegionLevel level)
        {
            level = RegionLevel.Continent;
            Rule found = null;
            if (sources == null) return false;
            foreach (var rule in sources)
            {
                if (rule == null || rule.source != source) continue;
                if (found != null) return false;
                found = rule;
            }
            if (found == null || !found.enabled) return false;
            level = precision == LocationPrecision.Precise && found.usePreciseLevel
                ? found.preciseMinimumLevel : found.minimumLevel;
            if (RegionLevels.Rank(level) < 0) return false;
            // Permission can reduce granularity, regardless of the underlying provider.
            if (precision == LocationPrecision.Approximate)
            {
                int rank = RegionLevels.Rank(approximateMinimumLevel);
                if (rank < 0) return false;
                if (rank < RegionLevels.Rank(level)) level = approximateMinimumLevel;
            }
            return true;
        }
    }
}

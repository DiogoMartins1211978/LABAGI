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

        [Serializable]
        public sealed class AccuracyLimit
        {
            public RegionLevel level;
            [Min(0), Tooltip("Maximum reported accuracy in metres. 0 accepts any usable accuracy at this level.")]
            public float maximumMetres;
        }

        [Header("Accuracy Limits")]
        public List<AccuracyLimit> accuracyLimits = new List<AccuracyLimit>
        {
            new AccuracyLimit { level = RegionLevel.Continent },
            new AccuracyLimit { level = RegionLevel.Country },
            new AccuracyLimit { level = RegionLevel.AdministrativeArea },
            new AccuracyLimit { level = RegionLevel.Locality, maximumMetres = 1000 },
            new AccuracyLimit { level = RegionLevel.Subdivision, maximumMetres = 250 },
            new AccuracyLimit { level = RegionLevel.CustomZone, maximumMetres = 100 }
        };

        [Header("Sources And Fallback Levels")]
        [Tooltip("The enabled switch always applies. Source and permission levels are fallbacks when accuracy is unavailable.")]
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

        // Explicit configuration and simulated coordinates can intentionally report zero error.
        public static bool HasUsableAccuracy(double? accuracyMetres, LocationOrigin source) =>
            accuracyMetres.HasValue && GeoGeometry.Finite(accuracyMetres.Value)
            && (accuracyMetres.Value > 0 || (accuracyMetres.Value == 0
                && (source == LocationOrigin.ConfigurationOverride || source == LocationOrigin.Simulated)));

        Rule FindSource(LocationOrigin source)
        {
            Rule found = null;
            if (sources == null) return null;
            foreach (var rule in sources)
            {
                if (rule == null || rule.source != source) continue;
                if (found != null) return null;
                found = rule;
            }
            return found != null && found.enabled ? found : null;
        }

        bool TryGetAccuracyLimit(RegionLevel level, out double maximum)
        {
            maximum = 0;
            AccuracyLimit found = null;
            if (accuracyLimits == null) return false;
            foreach (var limit in accuracyLimits)
            {
                if (limit == null || limit.level != level) continue;
                if (found != null) return false;
                found = limit;
            }
            if (found == null || !GeoGeometry.Finite(found.maximumMetres) || found.maximumMetres < 0) return false;
            maximum = found.maximumMetres;
            return true;
        }

        public bool TryGetMinimumLevel(LocationOrigin source, LocationPrecision precision, out RegionLevel level) =>
            TryGetMinimumLevel(source, precision, null, out level);

        public bool TryGetMinimumLevel(LocationOrigin source, LocationPrecision precision,
            double? accuracyMetres, out RegionLevel level)
        {
            level = RegionLevel.Continent;
            var rule = FindSource(source);
            if (rule == null) return false;
            if (HasUsableAccuracy(accuracyMetres, source))
            {
                // Evaluate geographic order explicitly; serialized enum values are not ordered.
                for (int i = RegionLevels.Ordered.Count - 1; i >= 0; i--)
                {
                    var candidate = RegionLevels.Ordered[i];
                    if (TryGetAccuracyLimit(candidate, out double maximum)
                        && (maximum == 0 || accuracyMetres.Value <= maximum))
                    { level = candidate; return true; }
                }
                return false;
            }
            level = precision == LocationPrecision.Precise && rule.usePreciseLevel
                ? rule.preciseMinimumLevel : rule.minimumLevel;
            if (RegionLevels.Rank(level) < 0) return false;
            if (precision == LocationPrecision.Approximate)
            {
                int rank = RegionLevels.Rank(approximateMinimumLevel);
                if (rank < 0) return false;
                if (rank < RegionLevels.Rank(level)) level = approximateMinimumLevel;
            }
            return true;
        }

        public bool Allows(ContentRegion region, LocationOrigin source, LocationPrecision precision,
            double? accuracyMetres)
        {
            if (region == null || FindSource(source) == null || RegionLevels.Rank(region.level) < 0) return false;
            if (!GeoGeometry.Finite(region.maximumAccuracyMetres) || region.maximumAccuracyMetres < 0) return false;
            if (HasUsableAccuracy(accuracyMetres, source))
            {
                // A region override replaces its level's default, allowing either stricter or looser limits.
                double maximum = region.maximumAccuracyMetres;
                if (maximum == 0 && !TryGetAccuracyLimit(region.level, out maximum)) return false;
                return maximum == 0 || accuracyMetres.Value <= maximum;
            }
            // A region with an explicit accuracy requirement cannot be accepted without that measurement.
            return region.maximumAccuracyMetres == 0 && TryGetMinimumLevel(source, precision, out var fallback)
                && RegionLevels.Rank(region.level) <= RegionLevels.Rank(fallback);
        }
    }
}

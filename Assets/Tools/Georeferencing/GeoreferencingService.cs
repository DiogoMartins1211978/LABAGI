using System;
using UnityEngine;

namespace Tools.Georeferencing
{
    public enum LocationState { Unavailable, Ready, Invalid, Stale, Inaccurate }

    public sealed class GeoreferencingService : MonoBehaviour
    {
        // --- CONFIGURABLE FIELDS ---

        [SerializeField] private RegionCatalog catalog;
        [SerializeField, Min(1)] private float maximumAgeSeconds = 30;
        [SerializeField, Min(0), Tooltip("Optional global accuracy cutoff; 0 disables it. Catalogue rules control source granularity.")]
        private float maximumAccuracyMetres = 0;

        // --- CURRENT RESULT ---

        public RegionResult Current { get; private set; } = new RegionResult();
        public LocationState State { get; private set; } = LocationState.Unavailable;
        public GeoPoint? Position { get; private set; }
        public double? AccuracyMetres { get; private set; }
        public double? TimestampUtcSeconds { get; private set; }
        public LocationOrigin Origin { get; private set; }
        public LocationPrecision Precision { get; private set; }
        public event Action<RegionResult> Changed;
        double lastTimestamp;

        public void SetPosition(double latitude, double longitude, double accuracyMetres, double utcSeconds,
            LocationOrigin origin = LocationOrigin.Unknown, LocationPrecision precision = LocationPrecision.Unknown)
        {
            if (!isActiveAndEnabled) return;
            var point = new GeoPoint(latitude, longitude);
            double age = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - utcSeconds;
            if (!point.IsValid || !GeoGeometry.Finite(utcSeconds) || age < -5)
            { Clear(LocationState.Invalid); return; }
            if (!GeoGeometry.Finite(accuracyMetres) || accuracyMetres < 0
                || (maximumAccuracyMetres > 0 && accuracyMetres > maximumAccuracyMetres))
            { Clear(LocationState.Inaccurate); return; }
            if (age > maximumAgeSeconds) { Clear(LocationState.Stale); return; }

            lastTimestamp = utcSeconds;
            Position = point;
            AccuracyMetres = accuracyMetres;
            TimestampUtcSeconds = utcSeconds;
            Origin = origin;
            Precision = precision;
            var resolved = RegionResolver.Resolve(catalog, point, origin, precision, accuracyMetres);
            State = resolved.IsValid ? LocationState.Ready : LocationState.Invalid;
            Current = resolved;
            Changed?.Invoke(Current);
        }

        public void Clear(LocationState state = LocationState.Unavailable)
        {
            State = state;
            Position = null;
            AccuracyMetres = null;
            TimestampUtcSeconds = null;
            Origin = LocationOrigin.Unknown;
            Precision = LocationPrecision.Unknown;
            Current = new RegionResult();
            Changed?.Invoke(Current);
        }

        void Update()
        {
            // Clear expired content even if the provider stops delivering samples.
            if (State == LocationState.Ready
                && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - lastTimestamp > maximumAgeSeconds)
                Clear(LocationState.Stale);
        }

        void OnDisable() => Clear();
    }
}

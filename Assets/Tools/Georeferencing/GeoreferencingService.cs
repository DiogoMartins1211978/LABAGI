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
        [SerializeField, Min(1)] private float maximumAccuracyMetres = 100;

        // --- CURRENT RESULT ---

        public RegionResult Current { get; private set; } = new RegionResult();
        public LocationState State { get; private set; } = LocationState.Unavailable;
        public event Action<RegionResult> Changed;
        double lastTimestamp;

        public void SetPosition(double latitude, double longitude, double accuracyMetres, double utcSeconds)
        {
            if (!isActiveAndEnabled) return;
            var point = new GeoPoint(latitude, longitude);
            double age = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - utcSeconds;
            if (!point.IsValid || !GeoGeometry.Finite(utcSeconds) || age < -5)
            { Clear(LocationState.Invalid); return; }
            if (!GeoGeometry.Finite(accuracyMetres) || accuracyMetres < 0 || accuracyMetres > maximumAccuracyMetres)
            { Clear(LocationState.Inaccurate); return; }
            if (age > maximumAgeSeconds) { Clear(LocationState.Stale); return; }

            lastTimestamp = utcSeconds;
            var resolved = RegionResolver.Resolve(catalog, point);
            State = resolved.IsValid ? LocationState.Ready : LocationState.Invalid;
            Current = resolved;
            Changed?.Invoke(Current);
        }

        public void Clear(LocationState state = LocationState.Unavailable)
        {
            State = state;
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

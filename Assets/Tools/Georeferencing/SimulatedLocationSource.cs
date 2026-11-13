using System;
using UnityEngine;

namespace Tools.Georeferencing
{
    public sealed class SimulatedLocationSource : MonoBehaviour
    {
        [SerializeField] private GeoreferencingService service;
        [SerializeField] private GeoPoint position = new GeoPoint(41.178, -8.608);
        [SerializeField] private bool updateContinuously = true;

        private GeoPoint lastPosition;
        private double nextUpdate;
        private bool hasApplied;

        [ContextMenu("Apply Simulated Position")]
        public void Apply()
        {
            if (service != null)
            {
                service.SetPosition(position.latitude, position.longitude, 0,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
                lastPosition = position;
                nextUpdate = Time.unscaledTimeAsDouble + 1;
                hasApplied = true;
            }
        }

        void Update()
        {
            if (!updateContinuously) return;

            // Apply edits immediately, then refresh once per second to keep the sample current.
            if (!hasApplied || position.latitude != lastPosition.latitude
                || position.longitude != lastPosition.longitude || Time.unscaledTimeAsDouble >= nextUpdate)
                Apply();
        }

        void OnDisable()
        {
            hasApplied = false;
            if (service != null) service.Clear();
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using Tools.Events;

namespace Tools.Georeferencing
{
    public sealed class RegionEvents : MonoBehaviour
    {
        [SerializeField] private GeoreferencingService service;
        [SerializeField] private MetricChannel regionEntered;
        [SerializeField] private MetricChannel regionExited;
        readonly HashSet<string> previous = new HashSet<string>();

        void OnEnable()
        {
            if (service == null) return;
            service.Changed += UpdateRegions;
            UpdateRegions(service.Current);
        }

        void UpdateRegions(RegionResult result)
        {
            var current = new HashSet<string>();
            foreach (var region in result.Regions) current.Add(region.regionId);
            // Only changes produce events, not every coordinate update.
            foreach (string id in previous)
                if (!current.Contains(id) && regionExited != null) regionExited.Raise(new MetricEvent(id));
            foreach (string id in current)
                if (!previous.Contains(id) && regionEntered != null) regionEntered.Raise(new MetricEvent(id));
            previous.Clear();
            previous.UnionWith(current);
        }

        void OnDisable()
        {
            if (service != null) service.Changed -= UpdateRegions;
            previous.Clear();
        }
    }
}

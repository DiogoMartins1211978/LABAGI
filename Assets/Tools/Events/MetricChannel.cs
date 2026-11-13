using System;
using UnityEngine;

namespace Tools.Events
{
    public readonly struct MetricEvent
    {
        // --- EVENT DATA ---

        public readonly string SubjectId;
        public readonly double Value;
        public readonly bool HasValue;

        public MetricEvent(string subjectId)
        {
            // This event identifies something but does not carry a measurement
            SubjectId = subjectId;
            Value = 0;
            HasValue = false;
        }

        public MetricEvent(string subjectId, double value)
        {
            SubjectId = subjectId;
            Value = value;
            HasValue = true;
        }
    }

    [CreateAssetMenu(menuName = "Events/Metric")]
    public sealed class MetricChannel : ScriptableObject
    {
        // --- EVENT ---

        public event Action<MetricEvent> Raised;

        // All listeners receive the same data
        public void Raise(MetricEvent data) => Raised?.Invoke(data);
    }
}

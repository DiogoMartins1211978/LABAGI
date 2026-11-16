using System;
using UnityEngine;

namespace Tools.Georeferencing
{
    [Serializable]
    public sealed class LocationOverride
    {
        public bool enabled;
        // Missing coordinates must not silently become a valid position at 0, 0.
        public double latitude = double.NaN;
        public double longitude = double.NaN;
        public double accuracyMetres;

        public bool IsValid => new GeoPoint(latitude, longitude).IsValid
            && GeoGeometry.Finite(accuracyMetres) && accuracyMetres >= 0;

        public static LocationOverride Parse(string json)
        {
            var result = new LocationOverride();
            JsonUtility.FromJsonOverwrite(json, result);
            if (result.enabled && !result.IsValid)
                throw new ArgumentException("Enabled location override requires valid latitude, longitude and accuracyMetres.");
            return result;
        }
    }
}

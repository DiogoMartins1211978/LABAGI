using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Tools.Georeferencing
{
    public sealed class AndroidLocationSource : MonoBehaviour
    {
        [SerializeField] private GeoreferencingService service;
        [SerializeField, Min(1)] private float initializationTimeout = 20;
        public string Status { get; private set; } = "Stopped";
        public LocationPrecision Precision { get; private set; } = LocationPrecision.Unknown;
        public bool HasFailed { get; private set; }
        public event Action<GeoPoint, double, double> PositionReceived;
        bool resumeAfterPause;

        public void StartLocation(GeoreferencingService target)
        {
            service = target;
            StartLocation();
        }

        // Startup and manual callers share this explicit acquisition entry point.
        public void StartLocation()
        {
            StopLocation();
            HasFailed = false;
            StartCoroutine(ReadLocation());
        }

        IEnumerator ReadLocation()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Status = "Requesting Permission";
            if (!HasPermission())
            {
                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                // Android answers each permission separately; coarse can arrive before fine.
                var answered = new HashSet<string>();
                callbacks.PermissionGranted += permission => answered.Add(permission);
                callbacks.PermissionDenied += permission => answered.Add(permission);
                UnityEngine.Android.Permission.RequestUserPermissions(new[]
                {
                    UnityEngine.Android.Permission.CoarseLocation,
                    UnityEngine.Android.Permission.FineLocation
                }, callbacks);
                float deadline = TimeoutDeadline();
                while (answered.Count < 2 && Time.realtimeSinceStartup < deadline) yield return null;
            }
            if (!HasPermission())
            { Fail("Permission Unavailable"); yield break; }
            Precision = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation)
                ? LocationPrecision.Precise : LocationPrecision.Approximate;
            Debug.Log("[Location] Android permission: " + Precision
                + "; Location enabled: " + Input.location.isEnabledByUser, this);
            if (!Input.location.isEnabledByUser) { Fail("Location Disabled"); yield break; }

            // Approximate permission uses the low-accuracy provider instead of requesting GPS.
            Input.location.Start(Precision == LocationPrecision.Precise ? 10 : 500, 5);
            Status = "Initializing";
            float timeout = TimeoutDeadline();
            while (Input.location.status == LocationServiceStatus.Initializing && Time.realtimeSinceStartup < timeout)
                yield return null;
            bool timedOut = Input.location.status == LocationServiceStatus.Initializing;
            while (Input.location.status == LocationServiceStatus.Running)
            {
                Status = "Running";
                PublishPosition(Input.location.lastData);
                yield return new WaitForSecondsRealtime(1);
            }
            Input.location.Stop();
            Fail(timedOut ? "Location Initialization Timed Out" : "Location Failed Or Stopped");
#else
            Fail("Android Device Required; Use Simulated Location In The Editor");
            yield break;
#endif
        }

        float TimeoutDeadline() => Time.realtimeSinceStartup + initializationTimeout;

        void Fail(string reason)
        {
            Status = reason;
            HasFailed = true;
            if (service != null) service.Clear();
        }

        void PublishPosition(LocationInfo sample)
        {
            if (service != null) service.SetPosition(sample.latitude, sample.longitude,
                sample.horizontalAccuracy, sample.timestamp, LocationOrigin.DeviceLocation, Precision);
            PositionReceived?.Invoke(new GeoPoint(sample.latitude, sample.longitude),
                sample.horizontalAccuracy, sample.timestamp);
        }

        public void StopLocation()
        {
            resumeAfterPause = false;
            HasFailed = false;
            StopAllCoroutines();
#if UNITY_ANDROID && !UNITY_EDITOR
            Input.location.Stop();
#endif
            Status = "Stopped";
            if (service != null) service.Clear();
        }

        void OnDisable() => StopLocation();
        void OnApplicationPause(bool paused)
        {
            // Let a pending permission request finish when the app returns.
            if (paused && (Status == "Running" || Status == "Initializing"))
            {
                StopLocation();
                resumeAfterPause = true;
                Status = "Paused";
            }
            else if (!paused && resumeAfterPause)
                StartLocation();
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static bool HasPermission() =>
            UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation)
            || UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.CoarseLocation);
#endif
    }
}

using System.Collections;
using UnityEngine;

namespace Tools.Georeferencing
{
    public sealed class AndroidLocationSource : MonoBehaviour
    {
        [SerializeField] private GeoreferencingService service;
        [SerializeField, Min(1)] private float initializationTimeout = 20;
        public string Status { get; private set; } = "Stopped";

        // Start explicitly from a user action; enabling the component does not request permission.
        public void StartLocation()
        {
            StopLocation();
            StartCoroutine(ReadLocation());
        }

        IEnumerator ReadLocation()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Status = "Requesting Permission";
            if (!HasPermission())
            {
                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                bool answered = false;
                callbacks.PermissionGranted += _ => answered = true;
                callbacks.PermissionDenied += _ => answered = true;
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.FineLocation, callbacks);
                float deadline = Time.realtimeSinceStartup + initializationTimeout;
                while (!answered && Time.realtimeSinceStartup < deadline) yield return null;
            }
            if (!HasPermission())
            { Status = "Permission Unavailable"; yield break; }
            if (!Input.location.isEnabledByUser) { Status = "Location Disabled"; yield break; }

            Input.location.Start(20, 5);
            Status = "Initializing";
            float timeout = Time.realtimeSinceStartup + initializationTimeout;
            while (Input.location.status == LocationServiceStatus.Initializing && Time.realtimeSinceStartup < timeout)
                yield return null;
            while (Input.location.status == LocationServiceStatus.Running)
            {
                Status = "Running";
                var sample = Input.location.lastData;
                if (service != null) service.SetPosition(sample.latitude, sample.longitude,
                    sample.horizontalAccuracy, sample.timestamp);
                yield return new WaitForSecondsRealtime(1);
            }
            Input.location.Stop();
            Status = "Location Failed Or Timed Out";
            if (service != null) service.Clear();
#else
            Status = "Android Device Required; Use Simulated Location In The Editor";
            yield break;
#endif
        }

        public void StopLocation()
        {
            StopAllCoroutines();
#if UNITY_ANDROID && !UNITY_EDITOR
            Input.location.Stop();
#endif
            Status = "Stopped";
            if (service != null) service.Clear();
        }

        void OnDisable() => StopLocation();
        void OnApplicationPause(bool paused) { if (paused) StopLocation(); }

#if UNITY_ANDROID && !UNITY_EDITOR
        static bool HasPermission() =>
            UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation)
            || UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.CoarseLocation);
#endif
    }
}

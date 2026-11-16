using System;
using System.IO;
using UnityEngine;

namespace Tools.Georeferencing
{
    [DisallowMultipleComponent]
    public sealed class LocationSource : MonoBehaviour
    {
        // --- CONFIGURABLE FIELDS ---

        [SerializeField] private GeoreferencingService service;
        public string ConfigurationPath => Path.Combine(Application.persistentDataPath, "location.json");
        public string Status => android != null && running && !locationOverride.enabled ? android.Status : status;
        public bool IsRunning => running && (locationOverride.enabled || android == null || !android.HasFailed);
        public LocationOrigin Origin { get; private set; } = LocationOrigin.Unknown;
        public LocationPrecision Precision { get; private set; } = LocationPrecision.Unknown;
        public bool IsPaused { get; private set; }
        public double ActiveSeconds => Math.Max(0,
            (IsPaused ? pausedAt : Time.realtimeSinceStartupAsDouble) - startedAt - pausedSeconds);
        public event Action<GeoPoint, double, double> PositionReceived;

        LocationOverride locationOverride = new LocationOverride();
        AndroidLocationSource android;
        string status = "Stopped";
        bool running;
        double nextRead;
        double startedAt, pausedAt, pausedSeconds;
        bool rejectedSampleLogged;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        WindowsLocationProvider windows;
#endif

        // A service is optional when only the raw location sample is needed.
        [ContextMenu("Start Location")]
        public void StartLocation()
        {
            StopLocation();
            Origin = LocationOrigin.Unknown;
            Precision = LocationPrecision.Unknown;
            if (!isActiveAndEnabled)
            { status = "Enable This Component"; return; }

            try
            {
                locationOverride = File.Exists(ConfigurationPath)
                    ? LocationOverride.Parse(File.ReadAllText(ConfigurationPath)) : new LocationOverride();
                running = true;
                rejectedSampleLogged = false;
                startedAt = Time.realtimeSinceStartupAsDouble;
                pausedSeconds = 0;
                if (IsPaused) pausedAt = startedAt;
                nextRead = 0;
                if (locationOverride.enabled)
                {
                    Origin = LocationOrigin.ConfigurationOverride;
                    status = "Configuration Override";
                    ReadPosition();
                    return;
                }
#if UNITY_ANDROID && !UNITY_EDITOR
                android = GetComponent<AndroidLocationSource>();
                if (android == null) android = gameObject.AddComponent<AndroidLocationSource>();
                android.PositionReceived += PublishAndroidPosition;
                android.StartLocation(null);
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                windows = new WindowsLocationProvider();
                windows.Start();
                status = windows.Status;
#else
                throw new PlatformNotSupportedException("System location is available on Windows and Android.");
#endif
            }
            catch (Exception error) { Fail(error); }
        }

        void Update()
        {
            if (running && !IsPaused && Time.unscaledTimeAsDouble >= nextRead) ReadPosition();
        }

        void ReadPosition()
        {
            nextRead = Time.unscaledTimeAsDouble + 1;
            try
            {
                if (locationOverride.enabled)
                {
                    PublishPosition(new GeoPoint(locationOverride.latitude, locationOverride.longitude),
                        locationOverride.accuracyMetres, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
                }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                else if (windows != null)
                {
                    bool received = windows.TryRead(out var point, out double accuracy, out double timestamp);
                    status = windows.Status;
                    Origin = windows.Origin;
                    Precision = Origin == LocationOrigin.Obfuscated ? LocationPrecision.Approximate : LocationPrecision.Unknown;
                    if (received) PublishPosition(point, accuracy, timestamp);
                    else if (service != null && service.State == LocationState.Ready) service.Clear();
                }
#endif
            }
            catch (Exception error) { Fail(error); }
        }

        void PublishPosition(GeoPoint point, double accuracy, double timestamp)
        {
            double age = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - timestamp;
            if (!point.IsValid || !GeoGeometry.Finite(accuracy) || accuracy < 0
                || !GeoGeometry.Finite(timestamp) || age < -5 || age > 30)
            {
                // Report once per attempt so discarded sensor data does not disappear silently.
                if (!rejectedSampleLogged)
                {
                    rejectedSampleLogged = true;
                    Debug.LogWarning(System.FormattableString.Invariant(
                        $"[Location] Rejected sample: Valid coordinates {point.IsValid}; Accuracy {accuracy:R} m; Age {age:F1} s."), this);
                }
                return;
            }

            if (service != null) service.SetPosition(point.latitude, point.longitude, accuracy, timestamp, Origin, Precision);
            PositionReceived?.Invoke(point, accuracy, timestamp);
        }

        void PublishAndroidPosition(GeoPoint point, double accuracy, double timestamp)
        {
            // Unity does not identify GPS, Wi-Fi or cellular; retain the permission mode separately.
            Origin = LocationOrigin.DeviceLocation;
            Precision = android.Precision;
            PublishPosition(point, accuracy, timestamp);
        }

        [ContextMenu("Stop Location")]
        public void StopLocation()
        {
            running = false;
            if (android != null)
            {
                android.PositionReceived -= PublishAndroidPosition;
                android.StopLocation();
            }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            windows?.Dispose();
            windows = null;
#endif
            if (service != null) service.Clear();
            status = "Stopped";
        }

        [ContextMenu("Reload Location Configuration")]
        public void ReloadConfiguration() => StartLocation();

        [ContextMenu("Create Location Configuration")]
        public void CreateConfiguration()
        {
            // Keep the local override outside the project and preserve existing user edits.
            if (!File.Exists(ConfigurationPath))
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                File.WriteAllText(ConfigurationPath, "{\n  \"enabled\": false,\n  \"latitude\": 41.178,\n  \"longitude\": -8.608,\n  \"accuracyMetres\": 0\n}\n");
            }
            Debug.Log("[Location] Configuration: " + ConfigurationPath, this);
        }

        void Fail(Exception error)
        {
            StopLocation();
            status = string.IsNullOrWhiteSpace(error.Message)
                ? $"{error.GetType().Name} (HRESULT 0x{error.HResult:X8})" : error.Message;
            Debug.LogWarning("[Location] Location stopped: " + status + "\n" + error.StackTrace, this);
        }

        void OnDisable() => StopLocation();
        void OnApplicationPause(bool paused)
        {
            if (paused == IsPaused) return;
            // A system permission dialog or focus change must not cancel acquisition.
            double now = Time.realtimeSinceStartupAsDouble;
            if (paused) pausedAt = now;
            else pausedSeconds += now - pausedAt;
            IsPaused = paused;
        }
    }
}

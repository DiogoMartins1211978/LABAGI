using System.Collections;
using UnityEngine;

namespace Tools.Georeferencing
{
    public sealed class StartupLocation : MonoBehaviour
    {
        // --- SESSION SNAPSHOT ---

        public static GeoPoint? Position { get; private set; }
        public static double? AccuracyMetres { get; private set; }
        public static double? TimestampUtcSeconds { get; private set; }
        public static string Status { get; private set; } = "Not Started";
        public static LocationOrigin Origin { get; private set; }
        public static LocationPrecision Precision { get; private set; }
        public static RegionCatalog Catalog { get; private set; }
        public static RegionResult Current { get; private set; } = MissingPosition();
        public static event System.Action<RegionResult> Resolved;

        public static RegionResult Resolve(RegionCatalog catalog) => Position.HasValue
            ? RegionResolver.Resolve(catalog, Position.Value, Origin, Precision) : MissingPosition();

        static RegionResult MissingPosition()
        {
            var result = new RegionResult();
            result.Errors.Add("Startup location is unavailable.");
            return result;
        }

        [SerializeField] private string acquisitionStatus = "Not Started";
        LocationSource source;
        static StartupLocation instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession()
        {
            instance = null;
            Position = null;
            AccuracyMetres = null;
            TimestampUtcSeconds = null;
            Status = "Not Started";
            Origin = LocationOrigin.Unknown;
            Precision = LocationPrecision.Unknown;
            Catalog = null;
            Current = MissingPosition();
            Resolved = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            if (instance != null) return;
            // Keep the game's catalogue assignment in one shared asset, independent of scenes.
            var settings = Resources.Load<StartupLocationSettings>(StartupLocationSettings.ResourcePath);
            Catalog = settings != null ? settings.catalog : null;
            var owner = new GameObject("Startup Location");
            DontDestroyOnLoad(owner);
            instance = owner.AddComponent<StartupLocation>();
            Debug.Log("[Location] Startup capture created.", instance);
        }

        IEnumerator Start()
        {
            source = gameObject.AddComponent<LocationSource>();
            source.PositionReceived += Capture;
            Debug.Log("[Location] Requesting startup position.", this);
            source.StartLocation();

            // Give permission and GPS initialization time without blocking the menu.
            while (!Position.HasValue && source.IsRunning && source.ActiveSeconds < 45)
            {
                string nextStatus = source.IsPaused ? "Waiting For Application To Resume" : source.Status;
                if (nextStatus != acquisitionStatus)
                {
                    Status = acquisitionStatus = nextStatus;
                    Debug.Log("[Location] " + nextStatus, this);
                }
                yield return null;
            }

            if (!Position.HasValue)
            {
                Status = acquisitionStatus = source.IsRunning ? "Timed Out: " + source.Status : source.Status;
                Current = MissingPosition();
                Debug.LogWarning("[Location] Startup location unavailable: " + Status, this);
            }
            source.PositionReceived -= Capture;
            source.StopLocation();
        }

        void Capture(GeoPoint position, double accuracy, double timestamp)
        {
            if (Position.HasValue) return;
            Position = position;
            AccuracyMetres = accuracy;
            TimestampUtcSeconds = timestamp;
            Origin = source.Origin;
            Precision = source.Precision;
            string provider = source.Status;
            Status = acquisitionStatus = "Captured: " + provider;
            // Stop at the first sample. Keep the snapshot when providers clear their state.
            source.StopLocation();
            // Preserve the supplied precision and decimal points when copying coordinates.
            Debug.Log(System.FormattableString.Invariant(
                $"[Location] Startup Location: {position.latitude:R}, {position.longitude:R}; Accuracy {accuracy:R} m; Provider: {provider}; Origin: {Origin}; Permission: {Precision}."), this);
            ApplyCatalog();
        }

        void ApplyCatalog()
        {
            // The source and permission mode filter regions before their content IDs are combined.
            Current = Resolve(Catalog);
            if (!Current.IsValid)
                Debug.LogWarning("[Location] Catalogue resolution failed: " + string.Join("; ", Current.Errors), this);
            else
                Debug.Log("[Location] Catalogue: " + Catalog.name
                    + "; Regions: " + string.Join(", ", Current.Regions.ConvertAll(region => region.displayName))
                    + "; Content: " + string.Join(", ", Current.ContentIds), this);
            Resolved?.Invoke(Current);
        }

        void OnDisable()
        {
            if (source == null) return;
            source.PositionReceived -= Capture;
            source.StopLocation();
        }
    }
}

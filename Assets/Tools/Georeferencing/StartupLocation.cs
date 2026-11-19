using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        public static event System.Action Refreshing;

        public static RegionResult Resolve(RegionCatalog catalog) => Position.HasValue
            ? RegionResolver.Resolve(catalog, Position.Value, Origin, Precision, AccuracyMetres) : MissingPosition();

        static RegionResult MissingPosition()
        {
            var result = new RegionResult();
            result.Errors.Add("Player location is unavailable.");
            return result;
        }

        [SerializeField] private string acquisitionStatus = "Not Started";
        LocationSource source;
        static StartupLocation instance;
        string mainMenuScene = "Main Menu";
        bool started, acquiring, hasRequested;
        float refinementSeconds = 5;
        float targetAccuracyMetres = 100;
        double firstCandidateSeconds;
        Candidate? best;

        struct Candidate
        {
            public GeoPoint point;
            public double accuracy, timestamp;
            public LocationOrigin origin;
            public LocationPrecision precision;
            public string provider;
        }

        static bool IsFresh(Candidate sample)
        {
            double age = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - sample.timestamp;
            return age >= -5 && age <= 30;
        }

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
            Refreshing = null;
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
            if (settings != null)
            {
                instance.mainMenuScene = string.IsNullOrWhiteSpace(settings.mainMenuScene) ? "Main Menu" : settings.mainMenuScene;
                instance.refinementSeconds = GeoGeometry.Finite(settings.refinementSeconds) ? Mathf.Max(0, settings.refinementSeconds) : 5;
                instance.targetAccuracyMetres = GeoGeometry.Finite(settings.targetAccuracyMetres) ? Mathf.Max(0, settings.targetAccuracyMetres) : 100;
            }
            Debug.Log("[Location] Startup capture created.", instance);
        }

        void OnEnable() => SceneManager.activeSceneChanged += HandleSceneChanged;

        void Start()
        {
            started = true;
            BeginCapture("Startup");
        }

        void HandleSceneChanged(Scene previous, Scene current)
        {
            if (started && current.name == mainMenuScene) BeginCapture("Main Menu");
        }

        void BeginCapture(string reason)
        {
            // Reuse an in-flight attempt, including an open permission dialog.
            if (acquiring) return;
            acquiring = true;
            bool requestPermission = !hasRequested;
            hasRequested = true;
            best = null;
            firstCandidateSeconds = 0;
            Position = null;
            AccuracyMetres = TimestampUtcSeconds = null;
            Origin = LocationOrigin.Unknown;
            Precision = LocationPrecision.Unknown;
            Current = MissingPosition();
            Status = acquisitionStatus = "Requesting Position";
            Refreshing?.Invoke();
            Debug.Log("[Location] Requesting location: " + reason + ".", this);
            StartCoroutine(Acquire(requestPermission));
        }

        IEnumerator Acquire(bool requestPermission)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // A menu revisit may observe a permission change, but must not ask again after a refusal.
            if (!requestPermission
                && !UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation)
                && !UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.CoarseLocation))
            {
                FailCapture("Permission Unavailable");
                yield break;
            }
#endif
            if (source == null) source = gameObject.AddComponent<LocationSource>();
            source.PositionReceived += Capture;
            source.StartLocation();

            // Prefer a better fix briefly without blocking menus or counting paused time.
            while (source.IsRunning && source.ActiveSeconds < 45)
            {
                if (best.HasValue && !IsFresh(best.Value)) best = null;
                if (best.HasValue && (source.ActiveSeconds - firstCandidateSeconds >= refinementSeconds
                    || (LocationRules.HasUsableAccuracy(best.Value.accuracy, best.Value.origin)
                        && best.Value.accuracy <= targetAccuracyMetres))) break;
                string nextStatus = source.IsPaused ? "Waiting For Application To Resume"
                    : best.HasValue ? "Refining Startup Position" : source.Status;
                if (nextStatus != acquisitionStatus)
                {
                    Status = acquisitionStatus = nextStatus;
                    Debug.Log("[Location] " + nextStatus, this);
                }
                yield return null;
            }
            string finalStatus = source.IsRunning ? "Timed Out: " + source.Status : source.Status;
            source.PositionReceived -= Capture;
            source.StopLocation();
            acquiring = false;
            if (best.HasValue && IsFresh(best.Value))
                FinishCapture(best.Value);
            else
            {
                FailCapture(finalStatus);
            }
        }

        void FailCapture(string status)
        {
            acquiring = false;
            Status = acquisitionStatus = status;
            Current = MissingPosition();
            Debug.LogWarning("[Location] Player location unavailable: " + Status, this);
            Resolved?.Invoke(Current);
        }

        void Capture(GeoPoint position, double accuracy, double timestamp)
        {
            bool usable = LocationRules.HasUsableAccuracy(accuracy, source.Origin);
            if (best.HasValue && IsFresh(best.Value))
            {
                bool previousUsable = LocationRules.HasUsableAccuracy(best.Value.accuracy, best.Value.origin);
                if (previousUsable && (!usable || accuracy >= best.Value.accuracy)) return;
            }
            else firstCandidateSeconds = source.ActiveSeconds;
            best = new Candidate { point = position, accuracy = accuracy, timestamp = timestamp,
                origin = source.Origin, precision = source.Precision, provider = source.Status };
        }

        void FinishCapture(Candidate sample)
        {
            Position = sample.point;
            AccuracyMetres = sample.accuracy;
            TimestampUtcSeconds = sample.timestamp;
            Origin = sample.origin;
            Precision = sample.precision;
            Status = acquisitionStatus = "Captured: " + sample.provider;
            Debug.Log(System.FormattableString.Invariant(
                $"[Location] Player Location: {sample.point.latitude:R}, {sample.point.longitude:R}; Accuracy {sample.accuracy:R} m; Provider: {sample.provider}; Origin: {Origin}; Permission: {Precision}."), this);
            ApplyCatalog();
        }

        void ApplyCatalog()
        {
            // Reported accuracy filters regions before their content IDs are combined.
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
            SceneManager.activeSceneChanged -= HandleSceneChanged;
            StopAllCoroutines();
            acquiring = false;
            if (source == null) return;
            source.PositionReceived -= Capture;
            source.StopLocation();
        }
    }
}

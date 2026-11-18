using UnityEngine;

namespace Tools.Georeferencing
{
    [CreateAssetMenu(menuName = "Georeferencing/Startup Settings")]
    public sealed class StartupLocationSettings : ScriptableObject
    {
        public const string ResourcePath = "Georeferencing/StartupLocationSettings";

        [Tooltip("Catalogue resolved after the startup position is captured. Its location rules are applied automatically.")]
        public RegionCatalog catalog;
        [Min(0), Tooltip("Keep the best fresh sample for up to this many active seconds after the first sample.")]
        public float refinementSeconds = 5;
        [Min(0), Tooltip("Finish early when a sample reaches this reported accuracy in metres.")]
        public float targetAccuracyMetres = 100;
    }
}

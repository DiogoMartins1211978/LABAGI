using UnityEngine;

namespace Tools.Georeferencing
{
    [CreateAssetMenu(menuName = "Georeferencing/Startup Settings")]
    public sealed class StartupLocationSettings : ScriptableObject
    {
        public const string ResourcePath = "Georeferencing/StartupLocationSettings";

        [Tooltip("Catalogue resolved after the startup position is captured. Its location rules are applied automatically.")]
        public RegionCatalog catalog;
    }
}

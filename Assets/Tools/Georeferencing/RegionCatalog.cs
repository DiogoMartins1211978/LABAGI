using System.Collections.Generic;
using UnityEngine;

namespace Tools.Georeferencing
{
    [CreateAssetMenu(menuName = "Georeferencing/Region Catalog")]
    public sealed class RegionCatalog : ScriptableObject
    {
        public List<ContentRegion> regions = new List<ContentRegion>();
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using Tools.Georeferencing;

namespace Tools.Content
{
    // Gameplay queries content; producers supply IDs without exposing their location logic.
    public static class ContentAvailability
    {
        static ContentCatalog catalog;
        static readonly HashSet<string> matchedRegions = new HashSet<string>(StringComparer.Ordinal);
        static IReadOnlyList<string> matchedRegionIds = Array.Empty<string>();
        static RegionCatalog resolvedCatalog;
        static string lastError;

        public static bool IsReady { get; private set; }
        public static IReadOnlyList<string> MatchedRegionIds => matchedRegionIds;
        public static RegionCatalog RegionCatalog => GetCatalog()?.regionCatalog;
        public static event Action Changed;

        public static void BeginRegionRefresh()
        {
            bool changed = IsReady || matchedRegions.Count > 0;
            resolvedCatalog = null;
            matchedRegions.Clear();
            matchedRegionIds = Array.Empty<string>();
            IsReady = false;
            if (changed) Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession()
        {
            catalog = null;
            matchedRegions.Clear();
            matchedRegionIds = Array.Empty<string>();
            resolvedCatalog = null;
            IsReady = false;
            lastError = null;
            Changed = null;
        }

        public static void SetRegions(RegionCatalog source, IEnumerable<string> ids)
        {
            var next = new HashSet<string>(StringComparer.Ordinal);
            if (ids != null)
                foreach (string id in ids)
                    if (!string.IsNullOrWhiteSpace(id)) next.Add(id);
            bool changed = !IsReady || resolvedCatalog != source || !matchedRegions.SetEquals(next);
            resolvedCatalog = source;
            matchedRegions.Clear();
            matchedRegions.UnionWith(next);
            var sorted = new List<string>(matchedRegions);
            sorted.Sort(StringComparer.Ordinal);
            matchedRegionIds = sorted.AsReadOnly();
            IsReady = true;
            if (changed) Changed?.Invoke();
        }

        static ContentCatalog GetCatalog()
        {
            if (catalog == null) catalog = Resources.Load<ContentCatalog>(ContentCatalog.ResourcePath);
            string error = catalog == null ? "Assign a ContentCatalog asset at Resources/ContentCatalog."
                : string.Join("; ", catalog.Validate());
            if (error.Length == 0) { lastError = null; return catalog; }
            if (lastError != error) Debug.LogWarning("[Content] " + error);
            lastError = error;
            return null;
        }

        static HashSet<string> GetAvailableIds(ContentCatalog source)
        {
            var available = new HashSet<string>(StringComparer.Ordinal);
            if (source == null) return available;
            foreach (var entry in source.entries)
                if (source.AllowsRegion(entry, resolvedCatalog, matchedRegions)) available.Add(entry.id);

            // Remove blocked parents repeatedly so requirements are checked through the whole chain.
            bool changed;
            do
            {
                changed = false;
                foreach (var entry in source.entries)
                {
                    if (!available.Contains(entry.id)) continue;
                    foreach (string required in entry.requiredIds)
                        if (!available.Contains(required))
                        { available.Remove(entry.id); changed = true; break; }
                }
            } while (changed);
            return available;
        }

        public static IReadOnlyList<string> AvailableIds
        {
            get
            {
                var ids = new List<string>(GetAvailableIds(GetCatalog()));
                ids.Sort(StringComparer.Ordinal);
                return ids.AsReadOnly();
            }
        }

        public static bool IsAvailable(string id) => TryGet<UnityEngine.Object>(id, out _);

        public static string GetUnavailableReason(string id)
        {
            var source = GetCatalog();
            return source == null ? "Invalid Content Catalogue" : source.GetUnavailableReason(id, resolvedCatalog, matchedRegions);
        }

        public static bool TryGet<T>(string id, out T asset) where T : UnityEngine.Object
        {
            asset = null;
            var source = GetCatalog();
            if (source == null) return false;
            if (!GetAvailableIds(source).Contains(id)) return false;
            foreach (var entry in source.entries)
                if (entry.id == id && entry.asset is T match)
                { asset = match; return true; }
            return false;
        }

        public static IReadOnlyList<T> GetAvailable<T>(string category) where T : UnityEngine.Object
        {
            var assets = new List<T>();
            var source = GetCatalog();
            var available = GetAvailableIds(source);
            if (source != null)
                foreach (var entry in source.entries)
                    if (entry.category == category && available.Contains(entry.id) && entry.asset is T asset) assets.Add(asset);
            return assets.AsReadOnly();
        }
    }
}

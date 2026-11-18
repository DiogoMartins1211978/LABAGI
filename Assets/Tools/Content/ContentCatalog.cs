using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Tools.Georeferencing;

namespace Tools.Content
{
    [CreateAssetMenu(menuName = "Content/Catalog")]
    public sealed class ContentCatalog : ScriptableObject
    {
        public const string ResourcePath = "ContentCatalog";

        [Serializable]
        public sealed class Entry
        {
            public string id;
            [Tooltip("Content classification used by the browser and runtime queries.")]
            public string category;
            public UnityEngine.Object asset;
            [Tooltip("Apply this entry's allowed and excluded region codes.")]
            public bool requiresRegion;
            [Tooltip("Accepted region codes that unlock this content, including inherited parent regions.")]
            public List<string> allowedRegionIds = new List<string>();
            [Tooltip("Excluded region codes. A more specific explicit rule overrides an inherited rule.")]
            public List<string> excludedRegionIds = new List<string>();
            [Tooltip("Content IDs required to use this entry. Optional rewards and random pools do not belong here.")]
            public List<string> requiredIds = new List<string>();
        }

        public RegionCatalog regionCatalog;
        public List<Entry> entries = new List<Entry>();

        public void SetRegionCatalog(RegionCatalog value)
        {
            if (regionCatalog == value) return;
            regionCatalog = value;
            // Codes belong to one catalogue. Keep the restriction enabled until new rules are chosen.
            foreach (var entry in entries ?? new List<Entry>())
            {
                if (entry == null) continue;
                entry.allowedRegionIds = new List<string>();
                entry.excludedRegionIds = new List<string>();
            }
        }

        public bool AllowsRegion(Entry entry, RegionCatalog resolvedCatalog, ISet<string> matchedIds)
        {
            if (!entry.requiresRegion) return true;
            if (regionCatalog == null || resolvedCatalog != regionCatalog || matchedIds == null || matchedIds.Count == 0) return false;
            if (regionCatalog.regions == null) return false;
            if (entry.allowedRegionIds == null || entry.excludedRegionIds == null
                || (entry.allowedRegionIds.Count == 0 && entry.excludedRegionIds.Count == 0)) return false;
            var matches = regionCatalog.regions.Where(region => region != null && matchedIds.Contains(region.regionId)).ToList();
            var ancestors = new HashSet<ContentRegion>();
            foreach (var region in matches)
            {
                var seen = new HashSet<ContentRegion>();
                for (var parent = region.parent; parent != null && seen.Add(parent); parent = parent.parent) ancestors.Add(parent);
            }

            // Resolve each matched branch from its deepest region; inherited parents must not vote twice.
            bool allowed = false;
            foreach (var region in matches)
            {
                if (ancestors.Contains(region)) continue;
                int rule = GetRegionRule(entry, region);
                if (rule < 0) return false;
                allowed |= rule > 0;
            }
            return allowed;
        }

        public int GetRegionRule(Entry entry, ContentRegion region)
        {
            if (entry == null || region == null) return 0;
            if (!entry.requiresRegion) return 1;
            var seen = new HashSet<ContentRegion>();
            for (var item = region; item != null && seen.Add(item); item = item.parent)
            {
                // The first explicit rule wins. Exclude only breaks a tie on the same region.
                if (entry.excludedRegionIds?.Contains(item.regionId) == true) return -1;
                if (entry.allowedRegionIds?.Contains(item.regionId) == true) return 1;
            }
            return (entry.allowedRegionIds?.Count ?? 0) == 0 && (entry.excludedRegionIds?.Count ?? 0) > 0 ? 1 : 0;
        }

        public string GetUnavailableReason(string id, RegionCatalog resolvedCatalog, ISet<string> matchedIds)
        {
            if (Validate().Count != 0) return "Invalid Content Catalogue";
            var byId = entries.ToDictionary(entry => entry.id, StringComparer.Ordinal);
            if (id == null || !byId.TryGetValue(id, out var selected)) return "Unknown Content: " + id;
            return Check(selected);

            string Check(Entry entry)
            {
                if (!AllowsRegion(entry, resolvedCatalog, matchedIds)) return entry.id + ": Region Rules";
                foreach (string required in entry.requiredIds)
                {
                    string reason = Check(byId[required]);
                    if (reason != null) return entry.id + " > " + reason;
                }
                return null;
            }
        }

        public List<string> Validate()
        {
            var errors = new List<string>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var regionIds = new HashSet<string>(StringComparer.Ordinal);
            if (regionCatalog != null && regionCatalog.regions != null)
                foreach (var region in regionCatalog.regions)
                    if (region != null) regionIds.Add(region.regionId);
            if (entries == null) { errors.Add("Content entries are missing."); return errors; }
            foreach (var entry in entries)
            {
                if (entry == null) { errors.Add("An empty content entry exists."); continue; }
                if (string.IsNullOrWhiteSpace(entry.id) || entry.id != entry.id.Trim() || !ids.Add(entry.id))
                    errors.Add("Missing, padded or duplicate content ID: " + entry.id);
                if (string.IsNullOrWhiteSpace(entry.category)) errors.Add("Missing category: " + entry.id);
                if (entry.asset == null) errors.Add("Missing asset: " + entry.id);
                if ((entry.category == "rooms" || entry.category == "enemies")
                    && (!(entry.asset is GameObject prefab) || prefab.transform.parent != null))
                    errors.Add("Assign a complete prefab root: " + entry.id);
                if (entry.requiredIds == null) errors.Add("Missing required content list: " + entry.id);
                if (entry.category == "cards" && entry.requiredIds?.Count > 0)
                    errors.Add("Cards cannot require other content: " + entry.id);
                if (entry.allowedRegionIds == null || entry.excludedRegionIds == null)
                    errors.Add("Missing region lists: " + entry.id);
                else
                    foreach (string regionId in entry.allowedRegionIds.Concat(entry.excludedRegionIds))
                        if (string.IsNullOrWhiteSpace(regionId) || !regionIds.Contains(regionId))
                            errors.Add("Unknown region code: " + entry.id + " > " + regionId);
            }

            if (errors.Count != 0) return errors;
            var byId = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (var entry in entries) byId.Add(entry.id, entry);
            foreach (var entry in entries)
                foreach (string required in entry.requiredIds)
                    if (string.IsNullOrWhiteSpace(required) || !byId.ContainsKey(required))
                        errors.Add("Unknown required content: " + entry.id + " > " + required);

            // A circular requirement cannot be satisfied by choosing either entry first.
            if (errors.Count != 0) return errors;
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var complete = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries) Visit(entry);
            return errors;

            void Visit(Entry entry)
            {
                if (complete.Contains(entry.id)) return;
                if (!visiting.Add(entry.id)) { errors.Add("Circular content requirement: " + entry.id); return; }
                foreach (string required in entry.requiredIds) Visit(byId[required]);
                visiting.Remove(entry.id);
                complete.Add(entry.id);
            }
        }
    }
}

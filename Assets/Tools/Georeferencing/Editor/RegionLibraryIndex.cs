using System;
using System.Collections.Generic;

namespace Tools.Georeferencing.Editor
{
    public sealed class RegionLibraryEntry
    {
        public string Id, ParentId, Name, GeometryJson, Description, DataSource;
        public RegionLevel Level;
    }

    public sealed class RegionLibraryIndex
    {
        public readonly List<RegionLibraryEntry> Entries;
        readonly Dictionary<string, RegionLibraryEntry> byId = new Dictionary<string, RegionLibraryEntry>(StringComparer.Ordinal);

        public RegionLibraryIndex(List<RegionLibraryEntry> entries)
        {
            Entries = entries;
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Id) || byId.ContainsKey(entry.Id))
                    throw new FormatException("Library Region IDs Must Be Present And Unique");
                if (string.IsNullOrWhiteSpace(entry.Name)) throw new FormatException("Library Region Names Must Be Present");
                if (RegionLevels.Rank(entry.Level) < 0) throw new FormatException("Unknown Library Region Level");
                byId.Add(entry.Id, entry);
            }
            foreach (var entry in entries)
                if (!string.IsNullOrEmpty(entry.ParentId)
                    && (!byId.TryGetValue(entry.ParentId, out var parent)
                        || RegionLevels.Rank(parent.Level) >= RegionLevels.Rank(entry.Level)))
                    throw new FormatException("Missing Or Invalid Library Parent: " + entry.Name);
        }

        public string PathLabel(RegionLibraryEntry entry)
        {
            var names = new List<string>();
            for (var current = entry; current != null; current = Parent(current)) names.Insert(0, current.Name);
            return string.Join(" / ", names);
        }

        RegionLibraryEntry Parent(RegionLibraryEntry entry) => string.IsNullOrEmpty(entry.ParentId) ? null : byId[entry.ParentId];

        public List<RegionLibraryEntry> Plan(RegionLibraryEntry selected, bool includeSubregions, ISet<string> existingIds)
        {
            var result = new List<RegionLibraryEntry>();
            var included = new HashSet<string>(existingIds, StringComparer.Ordinal);
            Action<RegionLibraryEntry> add = null;
            add = entry =>
            {
                if (included.Contains(entry.Id)) return;
                var parent = Parent(entry);
                if (parent != null) add(parent);
                included.Add(entry.Id);
                result.Add(entry);
            };
            add(selected);
            if (includeSubregions)
                foreach (var entry in Entries)
                    for (var parent = Parent(entry); parent != null; parent = Parent(parent))
                        if (parent == selected) { add(entry); break; }
            return result;
        }
    }
}

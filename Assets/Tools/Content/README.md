# Content Browser And Availability

Open **Tools > Content**, or select `Assets/Resources/ContentCatalog.asset` and choose **Open Content Browser**. The catalogue maps stable content IDs to assets; gameplay queries `ContentAvailability` without reading GPS data.

## Browse And Edit

Categories and the content search are on the left, thumbnails in the centre and details on the right. Drag the divider to resize the details panel. Cards use their illustration when available; other assets use Unity previews or icons. Double-click a thumbnail to locate its asset in the Project. **Locate Asset** and **Show Related** share the available width, stacking when the panel becomes narrow.

Search names, IDs, categories, requirements and configured region codes. Choose **Category** from its dropdown. **Used By** lists direct users of an entry. Maps, rooms and enemies also show **Requires** and **Show Related**; cards have no requirements of their own. Related entries are grouped by category and are clickable. **Show Related** stays highlighted while its filter is active; click it again or choose **Done** to clear the filter.

Use **Choose Required Content**, select multiple thumbnails, then choose **Done** or click the highlighted button again. The button stays highlighted during selection. Existing requirements and choices that create a cycle are disabled. **X** removes a requirement. **Add Entry** creates an entry in the chosen category. **Save Assets** saves changes; Undo restores edits. Removing an entry keeps its asset and leaves external references for review.

Rooms and enemies reference complete prefab roots. Their internal walls, props and visuals need no separate entries unless independently selected or restricted.

## Regions

The **Geographic Catalogue** field at the top of the window links the open content catalogue to exactly one geographic catalogue. This reference is shared by all entries; the included catalogue references **GameRegions**. Changing it clears all entries' allowed and excluded region codes; Undo restores the previous reference and lists. Geographic assets remain unchanged. Each entry's **Regions** submenu contains only its rules and preview.

Enable **Requires Region** to show the embedded map. Choose **Map Level** to edit countries, cities or another level. A single click selects and highlights a region for the preview. Double left-click toggles an allow rule; double right-click toggles an exclusion. Repeating the same double-click removes that explicit rule and restores inherited behaviour. Switching between allowed and excluded replaces the previous explicit state. Drag to pan and scroll to zoom. If custom regions overlap, double-click and choose the intended region from the menu.

The legend beside the zoom indicator shows allowed rules in green and exclusions in red, including their mouse buttons. The nearest explicit rule wins: Portugal allowed, Paranhos excluded and Campus explicitly allowed makes Campus and its unconfigured descendants available. Other areas of Paranhos remain excluded. With no allow list but some exclusions, other matched regions are allowed.

The explicit **Allowed Region IDs** and **Excluded Region IDs** appear in expandable lists below the map. Click an ID to select and frame it; **X** removes its explicit rule. Parent selections do not duplicate all descendant codes. **Frame Region** fits the selected region, **Frame Level** fits the chosen level and **Refresh Map** reloads the current view. Coordinate matching and boundary editing remain available in **Tools > Georeferencing**.

From Georeferencing, **Choose Content** opens the browser for the selected region; **Add To Region** adds its code to the selected entry's allow list and enables the restriction. The geographic catalogue must match the linked one.

| Rule | Result |
|---|---|
| Requires Region disabled | No direct geographic restriction |
| Explicit rule on a region | Replaces the rule inherited from its ancestors |
| No explicit rule on a region | Inherits its nearest configured ancestor |
| Allow list configured | Other branches remain restricted unless explicitly allowed |
| Exclusions only | Other matched regions of the linked catalogue are accepted |
| Both lists empty with Requires Region enabled | Restricted until codes are configured |
| No accepted regions or location unavailable | Region-restricted entries remain unavailable |

Evaluation starts at the most specific matched region and walks up its parent chain until an explicit rule is found. That rule passes to descendants without their own rules. If the same code appears in both lists, Exclude wins for that code. When unrelated matched branches overlap, an excluded branch blocks access. Rules use regions accepted by the catalogue's location rules and accuracy limits; unavailable finer regions cannot provide an override. Codes from another catalogue are not interchangeable.

**Preview:** below the map and legend shows the selected region's name in green when available or red when blocked by its rules or required content. Hover over the name for the blocking chain. The check includes ancestor regions. Coordinate matching and accuracy are tested in Georeferencing.

## Required Content And Optional Pools

Requirements are explicit. If a map requires an enemy and the enemy requires a card, all three must be available. A parent does not override a dependency's region restrictions. An incompatible required entry blocks its users; adjust the composition, the region rules or choose an available alternative.

Optional enemies and random rewards belong in selectable pools. Creators filter those pools using available content; a missing optional card need not block the map. Guaranteed rewards can be declared as requirements. Availability does not grant player ownership or deliver rewards.

`map.default` declares the five room types and `SnakeEnemy` used by its JSON. Keep these requirements in sync with the layout. The browser does not infer decks, reward pools or dependencies from gameplay asset references. New creators must declare required IDs or query available options.

## Runtime Queries

```csharp
using Tools.Content;
using UnityEngine;

var ids = ContentAvailability.AvailableIds;
var layouts = ContentAvailability.GetAvailable<TextAsset>("maps");

if (ContentAvailability.TryGet<GameObject>("SnakeEnemy", out var prefab))
{
    // Pass this prefab to the game's spawning logic.
}

// Null means available; otherwise the message identifies the blocking chain.
string reason = ContentAvailability.GetUnavailableReason("map.default");
```

After the startup attempt, `RegionContentBridge` resolves the content catalogue's linked geographic catalogue using the captured position and accuracy. `MatchedRegionIds` exposes accepted region codes. `IsReady` becomes true even if location fails. Entries without geographic restrictions remain usable when their requirements are satisfied. Subscribe to `Changed` to refresh pools; unsubscribe when the consumer is disabled. Data lasts for the current session.

Another provider can call `ContentAvailability.SetRegions(geographicCatalog, matchedRegionCodes)`. A different catalogue cannot grant access to restricted entries. Unknown content IDs and incompatible asset types return no asset. Invalid catalogues return no available content and report validation errors.

The existing card collection and dungeon generator retain their current creation flow. Creators control spawning, deck construction, ownership and rewards. Stable IDs must remain consistent across layouts, requirements and region rules.

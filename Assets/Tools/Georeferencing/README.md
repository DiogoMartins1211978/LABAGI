# Georeferencing

Create geographic regions and find the content IDs available at a position. Open **Tools > Georeferencing** to edit catalogues and test coordinates. The Editor map uses OpenStreetMap; runtime matching uses the region boundaries stored in the catalogue.

## Create And Edit Regions

1. Assign a catalogue or choose **New Catalogue**. New catalogues have a dedicated folder containing their regions and location rules.
2. Use **Add Region > Library** for existing boundaries, or **Add Region > Custom** to create your own. Library imports include missing parents; **Include Subregions** also imports descendants.
3. Select a region in the list or on the map to edit its details and content IDs. Double-click to frame its boundary.
4. For a custom boundary, use **Draw Polygon**, click at least three points and choose **Apply Polygon**. Alternatively, use **Circle At Test Position** and set its radius in kilometres, or **Import Boundary GeoJSON**. Drawing controls are disabled for library regions.
5. Choose **Save Assets** to save changes. **Remove Region** removes the selected branch from the catalogue while keeping its assets; Undo restores the entries.

Click the map to see matching **Regions** and **Content**. Editing Latitude or Longitude updates the test marker and matching content immediately; **Centre Map** moves the view to that position. Invalid coordinates hide the marker and leave the map centre unchanged. Latitude accepts -90 to 90 degrees; longitude accepts -180 to 180. The map loads automatically; drag to pan, use the wheel to zoom and use its top-right refresh button if needed. Coordinate matching also works without map access.

**Choose Content** opens the [Content Browser](../Content/README.md) for an editable region. Link that content catalogue to the same geographic catalogue using the **Geographic Catalogue** field at the top of the browser. Browse or search, select a thumbnail and use **Add To Region** or **Remove From Region** to edit the entry's allowed region codes. Exclusions and dependencies are also configured in the content browser. Geographic assets are not changed by these assignments.

The included [Demo Catalogue](Examples/README.md) returns Europe, Portugal, Porto District, Porto, Paranhos and Campus at `41.178, -8.608` when location rules are disabled. The [Region Library](../../../Tools/Georeferencing/RegionLibrary/README.md) contains additional boundaries and documents their sources. Library imports start with empty content lists.

The bundled demo is a read-only template in the tool. Create or select another catalogue, then choose **Populate From Demo** to fill it with independent regions, parent links and location rules. For a non-empty catalogue, confirm replacement to restore that base. Repeating the operation reuses its owned region assets; assets referenced by other catalogues remain untouched. Region IDs and content IDs are copied from the template. Undo restores the previous catalogue state.

## Game Catalogue

The game uses `Assets/Settings/Georeferencing/GameRegions/GameRegions.asset`, an independent copy of the demo with its own regions and rules. Changes to this catalogue do not change the demo. Its content IDs are copied from the demo and can be assigned to gameplay content when that integration is ready.

`Assets/Resources/Georeferencing/StartupLocationSettings.asset` selects the startup catalogue. Assign another catalogue there to change the game's configuration; selecting a catalogue in the Editor tool only changes the authoring view. The settings asset is loaded on both Windows and Android, without scene references.

## Levels And Content

The hierarchy runs from **Continent > Country > Administrative Area > Locality > Subdivision > Custom Zone**. Levels can be skipped. In the Porto dataset, districts use Administrative Area, municipalities use Locality and civil parishes use Subdivision; other datasets can use their own mapping.

Each region has a stable ID, an optional parent, a boundary and content IDs. A matching region includes its ancestors and combines their content IDs without duplicates. Parent links organise inheritance and do not clip a child's boundary.

| Boundary | Matching |
|---|---|
| Descendants Only | Included when a descendant matches |
| Circle | Within the configured centre and radius in kilometres |
| Polygon | Within any polygon outline, excluding its holes |

A region needs boundary data to match coordinates directly. Its name and level alone do not define its geographic extent. **Boundary Data Source / Licence** records the data attribution.

## Location Rules

Each catalogue references a `LocationRules` asset. Open **Location Rules** below the map to edit it. The demo and GameRegions use the same initial limits, stored in independent assets.

A coordinate must match the region's boundary. Reported horizontal accuracy then determines which matching regions can contribute content:

| Level | Maximum Reported Accuracy |
|---|---|
| Custom Zone | 100 m |
| Subdivision | 250 m |
| Locality | 1000 m |
| Administrative Area, Country, Continent | No cutoff |

The most specific accepted level is called **Minimum Level**; broader ancestors remain available. An accepted region also includes its ancestors. Accuracy is a provider estimate, so these limits define content policy rather than a guarantee of the device's true position.

A region's **Maximum Accuracy Metres** overrides its level's limit. Zero uses the catalogue default. For example, a zone can require 25 m, 50 m or 100 m. The reported coordinate must still be inside its boundary; accuracy does not expand a boundary or require an uncertainty circle to fit inside it.

Usable accuracy takes priority over source/permission granularity: Windows Wi-Fi at 70 m can reach Custom Zone, while Android Precise at 2000 m reaches Administrative Area. Source enabled switches still apply; missing, duplicate or disabled sources remain unavailable. If accuracy is missing or zero for a system provider, source and permission levels provide the fallback. ConfigurationOverride and Simulated can intentionally use zero error. An explicit region accuracy requirement needs a usable measurement.

To preview the result, enable **Apply Location Rules**, enter **Accuracy (m)**, and choose **Source** and **Permission**. The result lists eligible regions and content IDs; per-region overrides can differ from the default Minimum Level. Without a rules asset, matching remains geometric.

## Startup And Main Menu

The game automatically creates **Startup Location** and captures a position at startup and whenever the Main Menu scene becomes active. `StartupLocationSettings.mainMenuScene` selects that scene; the shared settings use `Main Menu`. No scene component or menu-button wiring is required. An in-flight attempt is reused instead of starting another one.

Each attempt retains the best fresh sample. After the first sample, it can refine for five active seconds, finishing early at 100 m or better; these values are configurable in `StartupLocationSettings`. It waits up to 45 active seconds without blocking the menu; application pauses suspend that timeout. The snapshot is reused during gameplay and cleared when a new menu capture begins. An unsuccessful attempt reports its status and can retry on the next menu visit.

After capturing a position, the service automatically resolves the configured catalogue using accuracy, region overrides and source/permission fallbacks. Read `StartupLocation.Current` for the permitted regions and content IDs, or subscribe to `StartupLocation.Resolved`. This event runs after each attempt, including failures through an invalid result. `StartupLocation.Refreshing` announces a new attempt; Content Availability clears its previous geographic matches until resolution finishes. `Catalog` identifies the configured asset; `Position`, `AccuracyMetres`, `Origin`, `Precision` and `Status` retain the latest snapshot.

Messages prefixed `[Location]` report acquisition and the catalogue result. On Android, open **Tools > Android Logs** and follow the [capture workflow](../../../ANDROID.md#diagnose-and-iterate). To consume the stored result:

```csharp
using Tools.Georeferencing;

// Startup has already applied the configured catalogue's location rules.
RegionResult result = StartupLocation.Current;

if (result.IsValid)
{
    // Pass these IDs to the game's content lookup.
    foreach (string contentId in result.ContentIds)
    {
        // Enable the corresponding content.
    }
}
```

`RegionContentBridge` resolves the geographic catalogue linked by [Content Availability](../Content/README.md) using the same startup snapshot. Content entries apply their allowed and excluded region codes, then their required content. Gameplay queries that service for available IDs or assets; region matching does not instantiate content. These rules are stored in the content catalogue, separately from a region's optional standalone Content IDs.

Before capture, during a refresh, or if acquisition fails, `Current.IsValid` is false. A valid result with no regions means the position matched no permitted region. `StartupLocation.Resolve(otherCatalog)` remains available for an additional query using the same snapshot.

Windows uses the system Geolocator. Enable location services and desktop-app access in Windows settings. Its reported source can be IPAddress, WiFi or another system provider. IP-derived positions can be several kilometres away; reported accuracy is provider metadata, not a measured error. Requests for higher accuracy cannot guarantee a better fix.

Android requests foreground location permission on the initial attempt and reads Unity's location service. The user can choose approximate or precise access. Menu revisits use the current permission without prompting again after a refusal; permission granted later in Android settings is recognised on the next visit. Precise permission allows finer acquisition but does not guarantee GPS accuracy. Project-wide build configuration and USB setup are documented in [Android](../../../ANDROID.md).

## Override The Position

`Application.persistentDataPath/location.json` supplies local test coordinates on Windows and Android. The **Create Location Configuration** context menu on `LocationSource` creates a disabled example and prints its exact path. A template also exists at `Tools/Georeferencing/location.example.json`.

```json
{
  "enabled": true,
  "latitude": 41.178,
  "longitude": -8.608,
  "accuracyMetres": 0
}
```

An enabled override replaces system acquisition and uses the `ConfigurationOverride` rule. Set `enabled` to `false` or remove the file to restore system location. Each startup or Main Menu attempt reads the file again; use **Reload Location Configuration** for a manually running source. Invalid enabled configuration reports an error. The live file stays outside the repository.

On Android, copy it to the app's `Application.persistentDataPath`, normally `/sdcard/Android/data/<application-id>/files/location.json`. Use the identifier in Player Settings and edit the template before copying it. Test precise access, approximate access, denied permission, disabled location services and the override separately on an installed APK. Return to the Main Menu to request a new capture; relaunch when testing the initial permission prompt. Unity Remote does not run the Android provider.

## Other Runtime Uses

For continuous acquisition, add `LocationSource` and call `StartLocation()` / `StopLocation()`. Its optional `GeoreferencingService` reference can remain empty when only raw samples are needed. To resolve continuously, assign a service with a catalogue and subscribe to `service.Changed`. Run one source per service; `SimulatedLocationSource` is available for Inspector-driven tests.

The service exposes `Current`, `State`, the accepted position and its metadata. It rejects invalid or stale samples. **Maximum Accuracy Metres = 0** disables its optional accuracy cutoff, allowing coarse positions to reach broad regions through location rules.

For direct queries, `RegionResolver.Resolve(catalog, point)` performs geometry matching; the overload accepting origin and permission also applies location rules. Check `IsValid` and `Errors` before using results.

`RegionEvents` can forward membership changes to metric channels as `region.entered` and `region.exited`, using region IDs as subjects. Assign the service and channels, then bind those channels on `MetricsRecorder` when integrating metrics.

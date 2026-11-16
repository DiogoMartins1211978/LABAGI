# Georeferencing

Create geographic regions and find the content IDs available at a position. Open **Tools > Georeferencing** to edit catalogues and test coordinates. The Editor map uses OpenStreetMap; runtime matching uses the region boundaries stored in the catalogue.

## Create And Edit Regions

1. Assign a catalogue or choose **New Catalogue**. New catalogues have a dedicated folder containing their regions and location rules.
2. Use **Add Region > Library** for existing boundaries, or **Add Region > Custom** to create your own. Library imports include missing parents; **Include Subregions** also imports descendants.
3. Select a region in the list or on the map to edit its details and content IDs. Double-click to frame its boundary.
4. For a custom boundary, use **Draw Polygon**, click at least three points and choose **Apply Polygon**. Alternatively, use **Circle At Test Position** and set its radius in kilometres, or **Import Boundary GeoJSON**. Drawing controls are disabled for library regions.
5. Choose **Save Assets** to save changes. **Remove Region** removes the selected branch from the catalogue while keeping its assets; Undo restores the entries.

Click the map to see matching **Regions** and **Content**. Editing Latitude or Longitude updates the test marker and matching content immediately; **Centre Map** moves the view to that position. Invalid coordinates hide the marker and leave the map centre unchanged. Latitude accepts -90 to 90 degrees; longitude accepts -180 to 180. The map loads automatically; drag to pan, use the wheel to zoom and use its top-right refresh button if needed. Coordinate matching also works without map access.

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

Each catalogue can reference a `LocationRules` asset. For an editable catalogue, **Location Rules**, below the map, opens it in the Inspector, creating one if missing. Rules can be shared between catalogues through the catalogue Inspector.

A source's **Minimum Level** is the most specific level permitted; broader ancestors remain eligible. These are editable content policies, not accuracy guarantees.

| Source | Unknown Permission | Approximate Permission | Precise Permission |
|---|---|---|---|
| DeviceLocation, IPAddress, Cellular, Default, Obfuscated | Administrative Area | Administrative Area | Custom Zone |
| WiFi | Locality | Administrative Area | Custom Zone |
| Satellite, ConfigurationOverride, Simulated | Custom Zone | Administrative Area | Custom Zone |
| Unknown | Disabled | Disabled | Disabled |

Each enabled source uses **Precise Minimum Level** for precise permission by default, including Cellular. Its **Use Precise Level** switch and precise level remain editable per source. Otherwise, **Minimum Level** applies. **Approximate Minimum Level** caps approximate samples at Administrative Area by default. Android reports `DeviceLocation` because Unity does not identify the underlying GPS, Wi-Fi or cellular provider; its permission mode selects the appropriate rule.

To preview filtering, enable **Apply Location Rules** below the map and choose **Source** and **Permission**. Only permitted regions contribute content IDs. Missing, duplicate or disabled source rules return no eligible content. A catalogue without a rules asset performs unfiltered matching.

## Startup Location

The game automatically creates **Startup Location**, requests one valid position and stops acquisition after capturing it. It waits up to 45 active seconds without blocking the menu; application pauses suspend that timeout. Scene changes preserve the snapshot. An unsuccessful attempt reports its status without automatically retrying.

After capturing a position, startup automatically resolves the configured catalogue with its source and permission rules. Read `StartupLocation.Current` for the permitted regions and content IDs, or subscribe to `StartupLocation.Resolved` before capture completes. `Catalog` identifies the configured asset; `Position`, `AccuracyMetres`, `Origin`, `Precision` and `Status` retain the raw snapshot.

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

`ContentIds` is the integration point for gameplay. Capturing a position does not activate game content automatically.

Before capture, or if acquisition fails, `Current.IsValid` is false. A valid result with no regions means the position matched no permitted region. `StartupLocation.Resolve(otherCatalog)` remains available for an additional query using the same snapshot.

Windows uses the system Geolocator. Enable location services and desktop-app access in Windows settings. Its reported source can be IPAddress, WiFi or another system provider. IP-derived positions can be several kilometres away; reported accuracy is provider metadata, not a measured error. Requests for higher accuracy cannot guarantee a better fix.

Android requests foreground location permission and reads Unity's location service. The user can choose approximate or precise access. Precise permission allows finer acquisition but does not guarantee GPS accuracy. Project-wide build configuration and USB setup are documented in [Android](../../../ANDROID.md).

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

An enabled override replaces system acquisition and uses the `ConfigurationOverride` rule. Set `enabled` to `false` or remove the file to restore system location. Restart the app after editing, or use **Reload Location Configuration** while the source is running. Invalid enabled configuration reports an error. The live file stays outside the repository.

On Android, copy it to the app's `Application.persistentDataPath`, normally `/sdcard/Android/data/<application-id>/files/location.json`. Use the identifier in Player Settings and edit the template before copying it. Test precise access, approximate access, denied permission, disabled location services and the override separately on an installed APK. Restart the app for each startup acquisition test; Unity Remote does not run the Android provider.

## Other Runtime Uses

For continuous acquisition, add `LocationSource` and call `StartLocation()` / `StopLocation()`. Its optional `GeoreferencingService` reference can remain empty when only raw samples are needed. To resolve continuously, assign a service with a catalogue and subscribe to `service.Changed`. Run one source per service; `SimulatedLocationSource` is available for Inspector-driven tests.

The service exposes `Current`, `State`, the accepted position and its metadata. It rejects invalid or stale samples. **Maximum Accuracy Metres = 0** disables its optional accuracy cutoff, allowing coarse positions to reach broad regions through location rules.

For direct queries, `RegionResolver.Resolve(catalog, point)` performs geometry matching; the overload accepting origin and permission also applies location rules. Check `IsValid` and `Errors` before using results.

`RegionEvents` can forward membership changes to metric channels as `region.entered` and `region.exited`, using region IDs as subjects. Assign the service and channels, then bind those channels on `MetricsRecorder` when integrating metrics.

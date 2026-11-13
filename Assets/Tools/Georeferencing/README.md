# Georeferencing

Create content regions, test coordinates and retrieve the content available at a position. Open **Tools > Georeferencing**. The tool operates independently of the game scenes.

## Quick Start

1. Assign the included `Examples/DemoRegions/DemoRegions.asset` catalogue, or select **Create Demo** and choose where to save a new one.
2. Select a region in the list or on the map. Selection highlights its border. Double-click a list entry or mapped region to frame its full boundary.
3. Set latitude `41.178` and longitude `-8.608`, select **Centre Map**, then click the centre of the map. The demo returns six regions: Europe, Portugal, Porto District, Porto, Paranhos and Campus.
4. The map loads automatically. Drag to pan and use the wheel to zoom; **Refresh Map** is overlaid in its top-right corner. Coordinate tests also work without map access.
5. Use **New Catalogue** and **Add Region** &gt; **Custom** for your own content. Save with **Save Assets**.
6. For existing geographic regions, use **Add Region** &gt; **Library**, select an entry and click **Add To Catalogue**. Enable **Include Subregions** to add a whole branch. Missing parents are included automatically.

Portugal and Spain use OpenStreetMap administrative boundaries, including islands and territorial waters. Europe uses OSM coastline data via OpenPlanetData, generalized for continental overview. The demo also contains the real Porto district, Porto municipality and its seven civil parishes. Campus is a synthetic demonstration polygon. The separate region library supplies the remaining Porto municipalities and subdivisions on request.

## Catalogue Folders And Selection

The catalogue selector stays on the left of the top bar, with its actions on the right. Each new catalogue gets a dedicated folder with its catalogue asset and a `Regions` subfolder. **Add Region** &gt; **Custom** starts its save dialog in that subfolder. Older catalogues can stay in their existing location; their dedicated regions folder is created beside them. Catalogue-dependent actions are disabled until a catalogue is assigned. Region actions live in the fixed-width **Region Details** panel and require a selected region.

The region list is grouped by level under expandable headings with entry counts. Search opens matching groups temporarily; selecting a region on the map opens its group. Rows show region names, with the full parent path in their tooltip. Double-click a row or a mapped region to frame its boundary.

Selecting a list entry updates **Regions** and **Content** for that region and its ancestors. Clicking the map queries all matches at the clicked coordinates. Both use the same selection and framing flow, while list selection leaves the coordinates unchanged. Use Latitude, Longitude and **Centre Map** to navigate to a position before clicking it.

**Remove Region** removes the selected region and its descendants from the current catalogue after confirmation. The assets remain in the project, and Unity Undo restores the catalogue entries. Descendants are included to preserve valid parent links among the remaining entries.

The map shows configured region boundaries with translucent fills. Off-screen polygons are excluded; display contours are reduced to sub-pixel detail at each zoom and cached for the current view. Each polygon is filled with its own holes, so large multipart regions remain visible when zoomed in. Full source coordinates are retained for matching and editing. Newly generated colours are distinct within a catalogue; `Region Color` can be changed manually, including to a colour already in use. Selection uses a brighter border. Organisational parents without their own boundary highlight their descendants, rather than inventing a geographic outline.

`Description` is free text about the region. `Boundary Data Source / Licence` records where its coordinates came from and the applicable licence; it does not affect region matching.

**Polygon Data** shows the polygon count initially. Expand it explicitly to bind and edit raw coordinate lists. Refresh the tool after changing source geometry outside its own Inspector.

## Regions And Content

Use the levels **Continent**, **Country**, **Administrative Area**, **Locality**, **Subdivision** and **Custom Zone**, in that order. Each asset has a stable ID, display name, optional parent and content IDs. A parent must be a broader level and belong to the same catalog. Levels can be skipped, so a custom zone can belong directly to a country.

These are content hierarchy categories, not universal administrative definitions. The bundled Porto data maps districts to Administrative Area, municipalities to Locality and civil parishes to Subdivision. Other datasets explicitly choose their mapping; the runtime has no Portuguese place-name or OSM admin-level rules. Existing City assets keep their serialized value and are now labelled Locality.

| Boundary | Behaviour |
|---|---|
| Descendants Only | Adds its content when a descendant matches; does not match coordinates itself |
| Circle | Matches coordinates within its centre/radius in kilometres |
| Polygon | Matches one or more polygon outlines, excluding holes |

A matching region also activates its ancestors. Content IDs are combined without duplicates. Parent links organise inheritance; they do not clip a child's boundary. A country needs an actual boundary to supply content everywhere in that country. An organisational parent alone supplies content only where its configured descendants match.

The catalog includes only the regions you create or import. The tool does not bundle every country or city. Region names and levels do not automatically supply geographical boundaries.

## Region Library

**Add Region** &gt; **Library** opens a searchable list of available regions. Choose the target catalogue and an entry. **Add To Catalogue** creates independent region assets in that catalogue's `Regions` folder. **Include Subregions** adds its descendants too. Existing entries are matched by stable region ID and kept unchanged, so repeating an import creates no duplicates. Use **Save Assets** in the main window afterwards.

The bundled library contains Europe, Portugal, Spain, Porto district, 18 municipalities and 275 civil parishes from a dated OSM snapshot. It is stored as compressed GeoJSON under `Tools/Georeferencing/RegionLibrary`, outside player builds. Imported regions have empty content lists ready for game-specific IDs. The demo has thirteen region assets, including its Porto branch, and remains independent of this library.

**Open Library File** loads another self-contained library FeatureCollection. Its parent links and level mapping are explicit; it is different from importing several polygons as the boundary of one region. The schema and sources are documented in `Tools/Georeferencing/RegionLibrary/README.md`.

## Draw Or Import A Boundary

For a circle, select a region, set the test coordinates and click **Circle At Test Position**. Change `Radius Kilometres` in its properties.

For a custom polygon, select **Draw Polygon**, click at least three vertices, then **Apply Polygon**. Dragging still pans the map. **Clear Draft** removes unfinished points. Applying a polygon replaces the selected region's previous polygons; Unity Undo restores them. **Draw Polygon**, **Apply Polygon** and **Clear Draft** are disabled for library regions, including copied library assets and the geographic demo regions. Campus remains a custom region.

Use **Import Boundary GeoJSON** in Region Details to load a local Polygon, MultiPolygon, Feature or FeatureCollection. All polygons in the file become the selected region's boundary; choose a file containing the intended region. GeoJSON coordinates are longitude/latitude. Record the dataset's attribution in `Boundary Data Source / Licence`.

The **OSM Boundaries** helper in the map's bottom-right corner opens [OpenStreetMap France's polygon exporter](https://polygons.openstreetmap.fr/). Find the administrative relation on OpenStreetMap, enter its relation ID, export GeoJSON and import that file into the selected region. An administrative boundary may include territorial waters; it is not necessarily the coastline. Country and city boundaries are administrative data; a continent's extent follows a geographic convention rather than a shared administrative boundary.

Use valid, simple polygon rings. The importer preserves polygon holes and multipart boundaries, but does not repair self-intersections or administrative topology. Edges are straight in longitude/latitude; polar caps and globe-spanning rings need a specialised geographic library. No automatic place search or worldwide administrative dataset is included.

## Map Provider

The Editor automatically loads OpenStreetMap raster tiles for the current viewport when the window opens, the catalogue changes, the tool refreshes or the view moves. Movement is debounced; obsolete queued views are discarded. Tiles appear progressively. Each patch uses one texture: a ready tile replaces its cached fallback rather than drawing over it. Cached children can also fill disjoint patches during zoom-out. Visible tiles are cached under `Library/GeoreferencingMap` for at least seven days. Attribution remains visible. Map tiles are not copied into builds or used to determine region membership.

The map shows one finite Web Mercator world, without horizontal repetition. Pan stops at its edges; **World View** centres the full world at zoom 0. Empty space around the world remains a plain background when the viewport is larger than it. Clicks outside the world do not select regions or create polygon vertices.

Tiles and region overlays share the Web Mercator projection. Place names and the amount of detail at each zoom are baked into the provider's raster images. The standard OSM layer uses local names and cannot be switched to English by a request parameter. Imported region boundaries remain separate geographic data; they are not extracted from the images.

The map is a visual aid: editing a region does not modify OpenStreetMap. Use follows the [OSM tile policy](https://operations.osmfoundation.org/policies/tiles/), including attribution, caching and no bulk/offline downloads. The tool sends its own identifying User-Agent. A failed map request does not prevent authoring or testing coordinates.

## Use In A Scene

Add `GeoreferencingService` to an object and assign its catalog. Choose one position source:

- `SimulatedLocationSource`: assign the service and coordinates. It is explicitly simulated, suitable for Windows and Editor testing. Continuous updates run once per second, with immediate updates when coordinates change.
- `AndroidLocationSource`: assign the service and call `StartLocation()` from a user action. This requests location permission on Android, reads device samples and stops on pause/disable. Call `StartLocation()` again after resuming when wanted.
- Another provider can call `SetPosition(latitude, longitude, accuracyMetres, utcSeconds)` without changing the resolver.

Configure maximum age and accuracy on the service for the scale of your zones. Approximate permission may be sufficient for broad regions but fail a small zone's accuracy threshold. Invalid, stale, unavailable or inaccurate positions clear location-dependent content. The service exposes `State`, `Current` and a `Changed` event. Base-game content remains the game's responsibility.

```csharp
using Tools.Georeferencing;

// Subscribe while this consumer is active, then unsubscribe on disable.
service.Changed += OnRegionsChanged;

void OnRegionsChanged(RegionResult result)
{
    // Resolve these IDs through the game's own card/enemy/dungeon data.
    foreach (string contentId in result.ContentIds)
    {
        // Update available content.
    }
}
```

For a one-off query without scene components, use `RegionResolver.Resolve(catalog, new GeoPoint(latitude, longitude))`. Inspect `IsValid` and `Errors` before consuming results.

## Optional Metrics

Add `RegionEvents`, assign the service and two Metric channel assets. Bind them on `MetricsRecorder` as `region.entered` and `region.exited`. The subject is the region ID; there is no numeric payload. Events occur only when membership changes, including inherited regions. Coordinate values are not logged by this adapter.

## Validation And Limits

Use the coordinates in [Demo Catalogue](Examples/README.md) to check region matching. Catalogue validation reports invalid geometry and hierarchy errors in the tool.

The Android source needs permission, sensor and lifecycle testing on an Android device. Windows currently uses simulated or externally supplied coordinates; automatic Windows positioning is not implemented. The map and Inspector still require visual validation in the Unity Editor. The feature does not include AR positioning, location persistence or integration into existing gameplay.

The Editor importer uses the Newtonsoft JSON package already resolved in this project. No package manifest change is required.

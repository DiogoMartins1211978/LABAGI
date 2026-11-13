# Region Library

Open **Tools > Georeferencing**, assign a catalogue and select **Add Region** &gt; **Library**. Choose an entry and click **Add To Catalogue**. Missing ancestors are included; **Include Subregions** adds the selected branch. Repeating an import keeps existing regions unchanged. Save the resulting assets from the main window.

`EuropeAndPorto.geojson.gz` contains 297 source regions: Europe, Portugal, Spain, Porto district, its 18 municipalities and 275 civil parishes. Data was retrieved on 2026-11-12; the exact OSM snapshot timestamp is stored in the collection's metadata. The compressed library stays outside Unity assets and player builds. Only requested regions become catalogue assets.

## Levels

| Category | Porto Dataset Mapping |
|---|---|
| Continent | Europe |
| Country | Portugal, Spain |
| AdministrativeArea | Porto district |
| Locality | Municipality |
| Subdivision | Civil parish |
| CustomZone | User-defined content zone |

The mapping belongs to this dataset. OSM administrative numbers vary between countries and are retained as source metadata, not interpreted by the runtime. Libraries for other countries choose the categories explicitly. A parent must use a broader category; intermediate categories can be skipped.

## Add Another Library

Use **Open Library File** for a self-contained GeoJSON FeatureCollection, optionally gzip-compressed. Include all referenced parents. Each feature needs these properties:

```json
{
  "regionId": "provider:stable-id",
  "parentId": "provider:parent-id",
  "displayName": "Region Name",
  "level": "Subdivision",
  "description": "Short Region Description",
  "dataSource": "Provider, Dataset Version And Licence"
}
```

Use `parentId: null` for a root. Geometry is a GeoJSON Polygon or MultiPolygon in longitude/latitude order; use `geometry: null` for an organisational parent. IDs must be unique and stable across imports. Imported regions start with empty content IDs and receive independent, editable assets and colours in the target catalogue. Existing catalogue assets are matched by ID, so their boundaries, colours and content assignments are preserved.

Imported assets retain their source ID in `libraryRegionId`, independently of their game-facing region ID. Copies keep this provenance. The drawing window disables **Draw Polygon**, **Apply Polygon** and **Clear Draft** for these regions; create a custom region for hand-drawn zones.

## Sources And Generalization

Portugal and Spain use OSM relations `295480` and `1311341` through the [OSM France polygon exporter](https://polygons.openstreetmap.fr/). These administrative boundaries include territorial waters.

Porto district is relation `3459013`; its municipalities and parishes were extracted in one bounded query using the [Private.coffee Overpass instance](https://overpass.private.coffee/).

Europe is derived from OSM coastline via [OpenPlanetData](https://openplanetdata.com/datasets/boundaries/continents/) and follows that provider's continental cookie-cutter convention. It is not an administrative Europe relation or the European Union.

To keep the example compact, its outline uses **radial reduction at 0.002 degrees** followed by **Douglas-Peucker reduction at 0.003 degrees**; rings below 0.002 square degrees are omitted. The result is suitable for continental overview, with generalized small islands and coasts.

*Country and Porto subdivision geometries retain the supplied source coordinates.*

Source and derived geographic data are [OpenStreetMap contributors, ODbL 1.0](https://www.openstreetmap.org/copyright). Preserve attribution and the applicable share-alike terms when redistributing derived geographic datasets.

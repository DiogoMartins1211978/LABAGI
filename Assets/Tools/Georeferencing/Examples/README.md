# Demo Catalogue

Assign `DemoRegions/DemoRegions.asset` in **Tools > Georeferencing**. The catalogue has its own `Regions` folder. Double-click an entry to frame its complete boundary.

| Latitude | Longitude | Expected Regions | Expected Content Count |
|---|---|---|---|
| 41.178 | -8.608 | Europe, Portugal, Porto District, Porto, Paranhos, Campus | 6 |
| 41.185 | -8.608 | Europe, Portugal, Porto District, Porto, Paranhos | 5 |
| 41.1579 | -8.6291 | Europe, Portugal, Porto District, Porto, Matching Parish | 5 |
| 40.4168 | -3.7038 | Europe, Spain | 2 |
| 0 | 0 | None | 0 |

Portugal contains the OpenStreetMap administrative boundary from relation `295480`; Spain uses relation `1311341`. Both include territorial waters. Europe uses an OSM-derived coastline outline from OpenPlanetData, generalized for continental overview. Porto District uses relation `3459013`; Porto uses municipality relation `3372453`. The municipality's seven civil parishes are included as Subdivision assets. Campus is a synthetic polygon, not an administrative boundary. Each region has a demonstration content ID.

The included parishes are Aldoar, Foz do Douro e Nevogilde; Bonfim; Campanhã; Cedofeita, Santo Ildefonso, Sé, Miragaia, São Nicolau e Vitória; Lordelo do Ouro e Massarelos; Paranhos; and Ramalde. These reflect the bundled OSM snapshot. The Locality category represents the administrative municipality, rather than a separately inferred urban footprint.

For the entire district, create a separate catalogue, select **Add Region** &gt; **Library**, search for Porto District, enable **Include Subregions** and add it. This adds all 18 municipalities and 275 civil parishes together with Europe and Portugal. Imported source regions have empty content IDs until configured.

`TestBoundary.geojson` is a synthetic square with a hole. Import it into a new region to exercise polygon import. Coordinates 41.178, -8.608 fall in its hole and are excluded; 41.175, -8.615 fall inside its outline.

## Boundary Sources

The coordinate extracts are in `DemoRegions/Boundaries`. Portugal and Spain were retrieved through the OpenStreetMap France polygon exporter on 2026-11-12. Porto's boundaries use the same dated OSM extract as the region library. Offshore borders do not follow the coastline. Europe uses OpenPlanetData's OSM coastline clipped to its continental convention, retrieved on the same date. Its geometry uses radial reduction at 0.002 degrees, Douglas-Peucker reduction at 0.003 degrees and omits rings smaller than 0.002 square degrees. Small islands and fine coastal detail are generalized. Map display simplification is separate and does not change these source coordinates. These examples are not surveying or authoritative legal data.

- [OpenPlanetData Continent Boundaries](https://openplanetdata.com/datasets/boundaries/continents/)
- [OpenPlanetData Boundary Pipeline](https://github.com/openplanetdata/openplanetdata-boundaries)
- [Portugal OSM Relation](https://www.openstreetmap.org/relation/295480)
- [Spain OSM Relation](https://www.openstreetmap.org/relation/1311341)
- [OSM Polygon Exporter](https://polygons.openstreetmap.fr/)
- [OpenStreetMap Copyright And ODbL Licence](https://www.openstreetmap.org/copyright)

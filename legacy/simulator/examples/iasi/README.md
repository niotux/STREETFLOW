# Iași map example

Central Iași road extract: **2,776 geometry/junction nodes, 5,890 directed lanes**.

```sh
./build/streetflow examples/iasi/CITY.IN
./build/streetflow examples/iasi/CITY.IN --validate
./build/streetflow examples/iasi/CITY.IN --headless --duration 60
```

Source: [OpenStreetMap export API](https://api.openstreetmap.org/api/0.6/map?bbox=27.57,47.155,27.59,47.17), retrieved on 2026-10-09. The bounding box covers part of central Iași, not the entire city. The API returns complete intersecting ways, so some roads extend outside the box. The exact response is stored losslessly as `source.osm.gz`; it also contains non-road objects, which the importer filters out.

Rebuild offline from the saved snapshot:

```sh
python3 tools/import_osm.py --input examples/iasi/source.osm.gz --output /tmp/iasi-reimport
./build/streetflow /tmp/iasi-reimport/CITY.IN
```

Download the same region again:

```sh
python3 tools/import_osm.py --bbox 47.155,27.57,47.17,27.59 --download-api osm --output /tmp/iasi-fresh
```

For a larger urban road extract, use `--city iasi` with Overpass. The included example uses a smaller export because the Overpass endpoints failed during initial testing.

`MAP.json` records the data source, geographic scale, node ID mapping, and modeling limitations. The default scale is 5 m per simulation unit. Geographic coordinates are kept fixed automatically by the marker in `CITY.LAYOUT`. Traffic signals come from tagged nodes; timing, traffic demand, and junction behavior remain simulated. Turn restrictions and speed limits are not represented.

Map data and derived `CITY.IN`, `CITY.LAYOUT`, and `MAP.json`: **© OpenStreetMap contributors**, licensed under the [Open Database License (ODbL)](https://www.openstreetmap.org/copyright). This data license is separate from the program's source code.

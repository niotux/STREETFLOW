#!/usr/bin/env python3
"""Convert OpenStreetMap XML / Overpass JSON into a STREETFLOW city (stdlib only)."""
import argparse
from collections import Counter
from datetime import datetime, timezone
import json
import gzip
import math
from pathlib import Path
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET

ROAD_TYPES = set('motorway motorway_link trunk trunk_link primary primary_link secondary secondary_link tertiary tertiary_link unclassified residential living_street service'.split())
IASI_BBOX = (47.13, 27.52, 47.21, 27.65)  # south, west, north, east; urban extent, not administrative boundary
ATTRIBUTION = '© OpenStreetMap contributors — https://www.openstreetmap.org/copyright (ODbL)'
EARTH_RADIUS = 6371008.8


def read_map(path):
    """Keep OSM IDs as Python integers; the exporter remaps them to C++ int IDs."""
    nodes, ways = {}, []
    open_file = gzip.open if str(path).endswith('.gz') else open
    with open_file(path, 'rb') as stream:
        prefix = stream.read(256).lstrip()
    if prefix.startswith(b'{'):
        with open_file(path, 'rt', encoding='utf-8') as stream:
            data = json.load(stream)
        if data.get('remark'):
            raise ValueError('Overpass returned an error: ' + data['remark'])
        if not isinstance(data.get('elements'), list):
            raise ValueError('Expected Overpass JSON with an elements array (not GeoJSON)')
        for element in data['elements']:
            if element['type'] == 'node':
                nodes[int(element['id'])] = (float(element['lat']), float(element['lon']), element.get('tags', {}))
            elif element['type'] == 'way':
                ways.append((int(element['id']), element.get('nodes', []), element.get('tags', {})))
    else:
        with open_file(path, 'rb') as stream:
            root = ET.parse(stream).getroot()
        if root.tag != 'osm':
            raise ValueError('Expected an OSM XML document')
        for element in root:
            if element.tag == 'remark':
                raise ValueError('Overpass returned an error: ' + (element.text or 'unknown error'))
            if element.tag not in ('node', 'way', 'relation'):
                continue
            tags = {tag.attrib['k']: tag.attrib['v'] for tag in element.findall('tag')}
            if element.tag == 'node':
                nodes[int(element.attrib['id'])] = (float(element.attrib['lat']), float(element.attrib['lon']), tags)
            elif element.tag == 'way':
                ways.append((int(element.attrib['id']), [int(nd.attrib['ref']) for nd in element.findall('nd')], tags))
            element.clear()
    for lat, lon, _ in nodes.values():
        if not math.isfinite(lat) or not math.isfinite(lon) or not -90 <= lat <= 90 or not -180 <= lon <= 180:
            raise ValueError('Invalid latitude/longitude in map')
    return nodes, ways


def driving_road(tags):
    if tags.get('highway') not in ROAD_TYPES or tags.get('area') == 'yes':
        return False
    # More specific access tags override general access (e.g. access=no, motorcar=yes).
    access = next((tags[key] for key in ('motorcar', 'motor_vehicle', 'vehicle', 'access') if key in tags), 'yes')
    return access not in ('no', 'private', 'agricultural', 'forestry')


def lane_counts(tags):
    def count(key):
        value = tags.get(key, '')
        return int(value) if value.isdigit() and 1 <= int(value) <= 10 else None
    oneway = tags.get('oneway:motorcar', tags.get('oneway', '')).lower()
    if not oneway and (tags.get('junction') == 'roundabout' or tags.get('highway') == 'motorway'):
        oneway = 'yes'
    total = count('lanes')
    if oneway in ('yes', 'true', '1'):
        return count('lanes:forward') or total or 1, 0
    if oneway == '-1':
        return 0, count('lanes:backward') or total or 1
    forward, backward = count('lanes:forward'), count('lanes:backward')
    both = count('lanes:both_ways') or 0
    available = max(2, (total or 2) - both)
    return forward or max(1, available - backward if backward else (available + 1) // 2), backward or max(1, available - forward if forward else available // 2)


def distance(a, b):
    lat1, lon1, lat2, lon2 = map(math.radians, (a[0], a[1], b[0], b[1]))
    h = math.sin((lat2-lat1)/2)**2 + math.cos(lat1)*math.cos(lat2)*math.sin((lon2-lon1)/2)**2
    return 2 * EARTH_RADIUS * math.asin(math.sqrt(min(1, h)))


def convert(nodes, ways, meters_per_unit=5.0, largest_component=False):
    if not math.isfinite(meters_per_unit) or meters_per_unit <= 0:
        raise ValueError('meters-per-unit must be finite and positive')
    edges, used, skipped = [], set(), Counter()
    for way_id, refs, tags in sorted(ways):
        if not driving_road(tags):
            skipped['excluded_ways'] += 1
            continue
        if any(ref not in nodes for ref in refs):
            raise ValueError(f'Way {way_id} references missing nodes; export ways AND their nodes (out body; >; out skel qt;)')
        forward, backward = lane_counts(tags)
        for a, b in zip(refs, refs[1:]):
            length = distance(nodes[a], nodes[b]) / meters_per_unit
            if a == b or length < 1e-6:
                skipped['zero_length_segments'] += 1
                continue
            used.update((a, b))
            for start, end, count in ((a, b, forward), (b, a, backward)):
                if count:
                    light = nodes[end][2].get('highway') == 'traffic_signals'
                    edges.append((start, end, int(light), length, count, way_id))
    if not edges:
        raise ValueError('Map contains no usable motor-vehicle roads')
    if largest_component:
        adjacency = {node: set() for node in used}
        for a, b, *_ in edges:
            adjacency[a].add(b)
            adjacency[b].add(a)
        unseen, components = set(used), []
        while unseen:
            root = min(unseen)
            unseen.remove(root)
            component, stack = {root}, [root]
            while stack:
                for neighbour in adjacency[stack.pop()]:
                    if neighbour in unseen:
                        unseen.remove(neighbour)
                        component.add(neighbour)
                        stack.append(neighbour)
            components.append(component)
        used = max(components, key=lambda component: (len(component), -min(component)))
        edges = [edge for edge in edges if edge[0] in used]
        skipped['removed_components'] = len(components) - 1
    mapping = {osm_id: index + 1 for index, osm_id in enumerate(sorted(used))}
    lat0 = sum(nodes[node][0] for node in used) / len(used)
    lon0 = sum(nodes[node][1] for node in used) / len(used)
    # Local equirectangular projection in metres, north points up on the canvas.
    layout = [f'# streetflow:geographic {meters_per_unit:g}', '# node x y; local metres, north up', '# ' + ATTRIBUTION]
    for osm_id, node_id in mapping.items():
        lat, lon, _ = nodes[osm_id]
        x = EARTH_RADIUS * math.radians(lon-lon0) * math.cos(math.radians(lat0))
        y = -EARTH_RADIUS * math.radians(lat-lat0)
        layout.append(f'{node_id} {x:.6f} {y:.6f}')
    roads = ['# start end traffic_light_at_end length', '# ' + ATTRIBUTION,
             f'# One simulation unit = {meters_per_unit:g} metres; geographic CITY.LAYOUT preserves positions']
    for a, b, light, length, count, way_id in edges:
        for _ in range(count):
            roads.append(f'{mapping[a]} {mapping[b]} {light} {length:.9f} # OSM way {way_id}')
    metadata = {
        'attribution': ATTRIBUTION, 'meters_per_unit': meters_per_unit,
        'nodes': len(used), 'directed_lanes': len(roads)-3,
        'projection': {'name': 'local equirectangular, metres, north up', 'latitude': lat0, 'longitude': lon0},
        'osm_node_ids': {str(node_id): osm_id for osm_id, node_id in mapping.items()},
        'skipped': dict(skipped),
        'limitations': ['No turn restrictions, speed limits, directional signal tags, or real demand imported.',
                        'Missing lane tags default to one lane per allowed direction; odd totals favor forward.',
                        'Every geometry point is a simulator node; node crossing headways still apply.',
                        'Signals mark incoming segments at tagged nodes; signal schedules are simulated.',
                        'Access filtering is static; conditional and directional access are not modeled.'],
    }
    return roads, layout, metadata


def bbox_value(value):
    try:
        south, west, north, east = map(float, value.split(','))
        if not (-90 <= south < north <= 90 and -180 <= west < east <= 180):
            raise ValueError()
        return south, west, north, east
    except ValueError:
        raise argparse.ArgumentTypeError('Expected south,west,north,east in valid latitude/longitude ranges')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument('--input', type=Path, help='Local .osm XML or Overpass JSON file')
    source.add_argument('--city', choices=['iasi'], help='Download the Iași urban road extract')
    source.add_argument('--bbox', type=bbox_value, help='Download south,west,north,east (decimal degrees)')
    parser.add_argument('--endpoint', default='https://overpass-api.de/api/interpreter')
    parser.add_argument('--download-api', choices=['overpass', 'osm'], default='overpass', help='OSM export API is an alternative for small bounding boxes')
    parser.add_argument('--output', type=Path, default=Path('imported-city'))
    parser.add_argument('--meters-per-unit', type=float, default=5, help='Default 5: default speed 2 units/s equals 36 km/h')
    parser.add_argument('--largest-component', action='store_true', help='Keep only the largest connected road component (ignoring direction)')
    args = parser.parse_args()
    if not math.isfinite(args.meters_per_unit) or args.meters_per_unit <= 0:
        parser.error('--meters-per-unit must be finite and positive')
    try:
        bbox = IASI_BBOX if args.city else args.bbox
        downloaded = None
        if bbox:
            query = '[out:xml][timeout:120];way[highway](' + ','.join(map(str, bbox)) + ');(._;>;);out body;'
            if args.download_api == 'osm':
                south, west, north, east = bbox
                url = 'https://api.openstreetmap.org/api/0.6/map?' + urllib.parse.urlencode({'bbox': f'{west},{south},{east},{north}'})
            else:
                url = args.endpoint + '?' + urllib.parse.urlencode({'data': query})
            request = urllib.request.Request(url, headers={'User-Agent': 'STREETFLOW-OSM-importer/1.0'})
            print(f'Downloading map from {args.download_api}…', flush=True)
            with urllib.request.urlopen(request, timeout=150) as response:
                downloaded = response.read()
            # Keep the exact input so imports can be repeated without another network request.
            args.output.mkdir(parents=True, exist_ok=True)
            input_file = args.output / 'source.osm'
            input_file.write_bytes(downloaded)
        else:
            input_file = args.input
        roads, layout, metadata = convert(*read_map(input_file), args.meters_per_unit, args.largest_component)
        metadata.update({'source': str(input_file), 'bbox': bbox,
                         'download_endpoint': url.split('?')[0] if downloaded else None,
                         'imported_at': datetime.now(timezone.utc).isoformat()})
        args.output.mkdir(parents=True, exist_ok=True)
        (args.output / 'CITY.IN').write_text('\n'.join(roads) + '\n', encoding='utf-8')
        (args.output / 'CITY.LAYOUT').write_text('\n'.join(layout) + '\n', encoding='utf-8')
        (args.output / 'MAP.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        print(f"Wrote {metadata['nodes']} nodes and {metadata['directed_lanes']} directed lanes to {args.output}")
        print(f'streetflow {args.output / "CITY.IN"} --layout {args.output / "CITY.LAYOUT"}')
    except (OSError, ValueError, KeyError, TypeError, ET.ParseError) as error:
        parser.exit(1, f'import_osm: {error}\n')


if __name__ == '__main__':
    main()

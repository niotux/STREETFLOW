"""Offline importer checks: topology, directions, geometry, units and engine integration."""
import importlib.util
import json
import math
from pathlib import Path
import subprocess
import socket
import sys
import tempfile
import time
import unittest
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('import_osm', ROOT / 'tools/import_osm.py')
osm = importlib.util.module_from_spec(spec)
spec.loader.exec_module(osm)
binary = sys.argv.pop(1) if len(sys.argv) > 1 else None


class ImportTests(unittest.TestCase):
    def setUp(self):
        self.nodes = {5000000001: (47.16, 27.58, {}), 5000000002: (47.161, 27.58, {'highway': 'traffic_signals'}),
                      5000000003: (47.161, 27.581, {})}
        self.ways = [(99, list(self.nodes), {'highway': 'primary', 'lanes': '3', 'lanes:forward': '2', 'lanes:backward': '1'})]

    def test_topology_units_signals_and_geometry(self):
        roads, layout, info = osm.convert(self.nodes, self.ways)
        rows = [line.split('#')[0].split() for line in roads if not line.startswith('#')]
        self.assertEqual(len(rows), 6)
        self.assertEqual(info['osm_node_ids']['1'], 5000000001)
        self.assertEqual(sum(row[:3] == ['1', '2', '1'] for row in rows), 2)
        self.assertEqual(sum(row[:3] == ['3', '2', '1'] for row in rows), 1)
        self.assertTrue(all(row[2] == '0' for row in rows if row[1] != '2'))
        length = float(rows[0][3])
        self.assertAlmostEqual(length * 5, 111.195, places=2)
        positions = [list(map(float, line.split()[1:])) for line in layout if not line.startswith('#')]
        self.assertLess(positions[1][1], positions[0][1])  # north up
        self.assertGreater(positions[2][0], positions[1][0])
        self.assertEqual((roads, layout, info), osm.convert(self.nodes, self.ways))

    def test_oneways_and_lanes(self):
        self.assertEqual(osm.lane_counts({'oneway': '-1', 'lanes': '2'}), (0, 2))
        self.assertEqual(osm.lane_counts({'junction': 'roundabout', 'lanes': '2'}), (2, 0))
        self.assertEqual(osm.lane_counts({'junction': 'roundabout', 'oneway': 'no'}), (1, 1))
        self.assertEqual(osm.lane_counts({'highway': 'motorway'}), (1, 0))
        self.assertEqual(osm.lane_counts({'lanes': '5', 'lanes:both_ways': '1'}), (2, 2))
        self.assertEqual(osm.lane_counts({'lanes': 'bad'}), (1, 1))
        self.assertEqual(osm.lane_counts({'lanes': '4', 'lanes:backward': '3'}), (1, 3))
        roads, _, _ = osm.convert(self.nodes, [(1, list(self.nodes), {'highway': 'residential', 'oneway': '-1'})])
        self.assertEqual([row.split()[:2] for row in roads[3:]], [['2', '1'], ['3', '2']])

    def test_access_and_missing_nodes(self):
        for tags in ({'highway': 'footway'}, {'highway': 'construction'}, {'highway': 'residential', 'access': 'private'},
                     {'highway': 'service', 'motor_vehicle': 'no'}, {'highway': 'residential', 'area': 'yes'}):
            self.assertFalse(osm.driving_road(tags))
        self.assertTrue(osm.driving_road({'highway': 'residential', 'access': 'no', 'motorcar': 'yes'}))
        with self.assertRaisesRegex(ValueError, 'missing nodes'):
            osm.convert(self.nodes, [(1, [5000000001, 42], {'highway': 'residential'})])
        with self.assertRaisesRegex(ValueError, 'no usable'):
            osm.convert(self.nodes, [(1, [5000000001, 5000000001], {'highway': 'residential'})])
        for scale in (0, -1, math.nan, math.inf):
            with self.assertRaises(ValueError):
                osm.convert(self.nodes, self.ways, scale)

    def test_largest_component_and_no_false_crossings(self):
        nodes = {**self.nodes, 6: (47.16, 27.581, {}), 7: (47.162, 27.579, {})}
        ways = self.ways + [(100, [6, 7], {'highway': 'residential'})]
        _, _, info = osm.convert(nodes, ways)
        self.assertEqual(info['nodes'], 5)  # crossing lines never create a connection
        _, _, info = osm.convert(nodes, ways, largest_component=True)
        self.assertEqual(info['nodes'], 3)
        self.assertEqual(info['skipped']['removed_components'], 1)

    def test_xml_json_and_engine(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            xml = '<osm version="0.6">'
            elements = []
            for node_id, (lat, lon, tags) in self.nodes.items():
                xml += f'<node id="{node_id}" lat="{lat}" lon="{lon}">' + ''.join(f'<tag k="{k}" v="{v}"/>' for k, v in tags.items()) + '</node>'
                elements.append({'type': 'node', 'id': node_id, 'lat': lat, 'lon': lon, 'tags': tags})
            for way_id, refs, tags in self.ways:
                xml += f'<way id="{way_id}">' + ''.join(f'<nd ref="{ref}"/>' for ref in refs) + ''.join(f'<tag k="{k}" v="{v}"/>' for k, v in tags.items()) + '</way>'
                elements.append({'type': 'way', 'id': way_id, 'nodes': refs, 'tags': tags})
            (path / 'map.osm').write_text(xml + '</osm>')
            (path / 'map.json').write_text(json.dumps({'elements': elements}))
            self.assertEqual(osm.read_map(path / 'map.osm'), osm.read_map(path / 'map.json'))
            subprocess.run([sys.executable, str(ROOT / 'tools/import_osm.py'), '--input', str(path / 'map.osm'), '--output', str(path)], check=True, capture_output=True)
            if binary:
                result = subprocess.run([binary, str(path / 'CITY.IN'), '--layout', str(path / 'CITY.LAYOUT'), '--validate'], check=True, capture_output=True, text=True)
                self.assertIn('3 nodes, 6 directed lanes', result.stdout)
                subprocess.run([binary, str(path / 'CITY.IN'), '--layout', str(path / 'CITY.LAYOUT'), '--headless', '--duration', '10'], check=True, capture_output=True)
            (path / 'error.osm').write_text('<osm><remark>runtime error: timeout</remark></osm>')
            with self.assertRaisesRegex(ValueError, 'timeout'):
                osm.read_map(path / 'error.osm')

    def test_real_iasi_snapshot(self):
        sample = ROOT / 'examples/iasi'
        roads, layout, info = osm.convert(*osm.read_map(sample / 'source.osm.gz'))
        self.assertEqual('\n'.join(roads) + '\n', (sample / 'CITY.IN').read_text())
        self.assertEqual('\n'.join(layout) + '\n', (sample / 'CITY.LAYOUT').read_text())
        self.assertEqual((info['nodes'], info['directed_lanes']), (2776, 5890))
        if not binary:
            return
        result = subprocess.run([binary, str(sample / 'CITY.IN'), '--headless', '--duration', '60'], check=True, capture_output=True, text=True)
        stats = json.loads(result.stdout)
        self.assertGreater(stats['spawned'], 0)
        self.assertGreater(stats['completed'], 0)
        with socket.socket() as listener:
            listener.bind(('127.0.0.1', 0))
            port = listener.getsockname()[1]
        server = subprocess.Popen([binary, str(sample / 'CITY.IN'), '--port', str(port), '--no-browser', '--paused'], stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
        try:
            deadline = time.monotonic() + 5
            while True:
                try:
                    with urllib.request.urlopen(f'http://127.0.0.1:{port}/api/city', timeout=1) as response:
                        city = json.load(response)
                    break
                except OSError:
                    if time.monotonic() >= deadline or server.poll() is not None:
                        self.fail('Iași HTTP server failed to start')
                    time.sleep(.05)
            self.assertTrue(city['geographic'])
            self.assertEqual(city['metersPerUnit'], 5)
            self.assertIn('OpenStreetMap', city['attribution'])
            expected = {int(row.split()[0]): list(map(float, row.split()[1:])) for row in layout if not row.startswith('#')}
            for node in city['nodes']:
                self.assertAlmostEqual(node['x'], expected[node['id']][0], places=4)
                self.assertAlmostEqual(node['y'], expected[node['id']][1], places=4)
        finally:
            server.terminate()
            server.communicate(timeout=5)


if __name__ == '__main__':
    unittest.main()

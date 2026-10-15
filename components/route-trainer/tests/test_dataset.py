import copy
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

from route_trainer.dataset import validate
from route_trainer.projects import load_project, unpack

WORKSPACE = Path(__file__).resolve().parents[3]
EXAMPLE = Path(__file__).resolve().parents[1] / "examples" / "four-way-routes.json"


class DatasetTests(unittest.TestCase):
    def setUp(self):
        _, _, self.network = load_project(WORKSPACE / "experiments/drafts/tiny-graph")
        self.dataset = json.loads(EXAMPLE.read_text())

    def test_authored_periods_preserve_volume_and_leave_uncovered_hours_empty(self):
        rows, periods = validate(self.dataset, self.network)
        self.assertEqual([0, 25200, 32400, 57600, 68400], [period["start_seconds"] for period in periods])
        self.assertEqual([], periods[0]["arrivals"])
        self.assertEqual([], periods[2]["arrivals"])
        self.assertEqual("north_out", rows[0]["path"][-1])
        rates = {arrival["start_edge"]: arrival["vehicles_per_hour"] for arrival in periods[1]["arrivals"]}
        self.assertEqual({"north_in": 40, "south_in": 400}, rates)

    def test_invalid_records_are_not_repaired(self):
        for field, value in [("turns", [999]), ("turns", [True]), ("start_edge", "missing"), ("vehicles_per_hour", -1), ("end_seconds", 1)]:
            with self.subTest(field=field, value=value):
                dataset = copy.deepcopy(self.dataset)
                dataset["routes"][0][field] = value
                with self.assertRaises(ValueError):
                    validate(dataset, self.network)

    def test_changed_map_and_misspelled_fields_are_rejected(self):
        self.dataset["network_sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "different network"):
            validate(self.dataset, self.network)
        self.dataset["network_sha256"] = self.network.sha256
        self.dataset["routes"][0]["vechicles_per_hour"] = 100
        with self.assertRaisesRegex(ValueError, "Unknown route"):
            validate(self.dataset, self.network)

    def test_short_multilane_fragments_require_continuous_lanes(self):
        xml = '''<net>
          <edge id="a"><lane id="a_0" index="0" length="5" speed="10" shape="0,0 5,0"/></edge>
          <edge id="b"><lane id="b_0" index="0" length="5" speed="10" shape="5,0 10,0"/><lane id="b_1" index="1" length="5" speed="10" shape="5,1 10,1"/></edge>
          <edge id="c"><lane id="c_0" index="0" length="5" speed="10" shape="10,0 15,0"/></edge>
          <connection from="a" to="b" fromLane="0" toLane="0"/>
          <connection from="b" to="c" fromLane="1" toLane="0"/>
        </net>'''
        from route_trainer.network import Network
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "network.net.xml"
            path.write_text(xml)
            graph = Network(path)
            self.assertEqual(["a", "b"], graph.replay("a", [0]))
            with self.assertRaisesRegex(ValueError, "continuous"):
                graph.replay("a", [0, 0])
            path.write_text(xml.replace('length="5"', 'length="30"'))
            self.assertEqual(["a", "b", "c"], Network(path).replay("a", [0, 0]))

    def test_zip_cannot_escape_workspace(self):
        with tempfile.TemporaryDirectory() as temporary:
            archive = Path(temporary) / "input.zip"
            with zipfile.ZipFile(archive, "w") as output:
                output.writestr("../outside", "bad")
            with self.assertRaises(ValueError):
                unpack(archive, Path(temporary) / "contents")


if __name__ == "__main__":
    unittest.main()

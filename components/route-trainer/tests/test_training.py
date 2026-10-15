import json
import tempfile
import unittest
from pathlib import Path

from route_trainer.projects import export_project, load_project
from route_trainer.training import train

WORKSPACE = Path(__file__).resolve().parents[3]


class TrainingTests(unittest.TestCase):
    def test_cpu_training_exports_a_portable_time_conditioned_model(self):
        source, _, network = load_project(WORKSPACE / "experiments/drafts/tiny-graph")
        dataset = json.loads((Path(__file__).resolve().parents[1] / "examples/four-way-routes.json").read_text())
        with tempfile.TemporaryDirectory() as temporary:
            trained = Path(temporary) / "trained"
            model, report = train(network, dataset, trained, epochs=60)
            self.assertEqual(3, model["format_version"])
            self.assertEqual(network.sha256, model["network_sha256"])
            self.assertEqual(0, report["failed_routes"])
            self.assertLess(report["parameter_count"], 100000)
            self.assertGreater(report["routes_per_second"], 0)
            paths = report["sampled_paths"]
            self.assertLess(paths.get("period=1:north_in > west_out", 0), paths.get("period=3:north_in > west_out", 0))
            package = export_project(source, trained, Path(temporary) / "package", "test-neural", "Test neural model")
            manifest = json.loads((package / "experiment.json").read_text())
            self.assertEqual(3, manifest["format_version"])
            self.assertNotIn("demand_file", manifest)
            self.assertFalse((package / "demand.json").exists())
            self.assertTrue((package / "route-model.onnx").is_file())
            self.assertEqual(dataset, json.loads((package / "route-dataset.json").read_text()))


if __name__ == "__main__":
    unittest.main()

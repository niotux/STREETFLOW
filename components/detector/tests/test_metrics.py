import sys
from pathlib import Path
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "camera"))
from metrics import Measurements, inside, passage


def config(**extra):
    return dict(window_seconds=1, queue_duration_seconds=2,
        lanes=[dict(lane_id="lane", approach_id="north", polygon=[[0, 0], [1, 0], [1, 1], [0, 1]],
                    counting_line=[[0.1, 0.5], [0.9, 0.5]])], **extra)


def car(bottom=0.5, x=0.4):
    return (x, bottom - 0.2, x + 0.2, bottom)


class MeasurementTests(unittest.TestCase):
    def test_crossing_on_window_boundary_belongs_to_next_window(self):
        m = Measurements(config())
        m.update([car(0.45)], 0)
        first = m.update([car(0.5)], 1)
        second = m.update([car(0.55)], 2)
        self.assertEqual(first[3][0]["crossed_vehicle_count"], 0)
        self.assertEqual(second[3][0]["crossed_vehicle_count"], 1)

    def test_stationary_vehicle_requires_dwell_and_motion_resets_queue(self):
        m = Measurements(config())
        m.update([car()], 0)
        self.assertEqual(m.update([car()], 1)[3][0]["queued_vehicle_count"], 0)
        self.assertEqual(m.update([car()], 2)[3][0]["queued_vehicle_count"], 1)
        self.assertEqual(m.update([car(0.52)], 3)[3][0]["queued_vehicle_count"], 0)

    def test_empty_is_zero_and_outage_is_null(self):
        m = Measurements(config())
        m.update([], 0)
        lane = m.update([], 1)[3][0]
        self.assertEqual(lane["visible_vehicle_count"], 0)
        self.assertIsNone(lane["mean_speed_mps"])
        unavailable = m.unavailable(2)
        self.assertEqual(unavailable[2], "unavailable")
        self.assertIsNone(unavailable[3][0]["visible_vehicle_count"])

    def test_track_gap_does_not_invent_crossing_or_queue(self):
        m = Measurements(config())
        m.update([car(0.45)], 0)
        lane = m.update([car(0.55)], 4)[3][0]
        self.assertEqual(lane["crossed_vehicle_count"], 0)
        self.assertEqual(lane["queued_vehicle_count"], 0)
        self.assertEqual(lane["measurement_status"], "partial")

    def test_repeated_passages_count_but_reverse_traversals_do_not(self):
        m = Measurements(config())
        m.update([car(0.45)], 0)
        self.assertEqual(m.update([car(0.55)], 1)[3][0]["crossed_vehicle_count"], 1)
        self.assertEqual(m.update([car(0.45)], 2)[3][0]["crossed_vehicle_count"], 0)
        self.assertEqual(m.update([car(0.55)], 3)[3][0]["crossed_vehicle_count"], 1)

    def test_finite_line_and_region_membership(self):
        self.assertIsNone(passage((0, 0.4), (0, 0.6), [[0.1, 0.5], [0.9, 0.5]]))
        self.assertFalse(inside((1.5, 0.5), config()["lanes"][0]["polygon"]))

    def test_missing_line_is_unsupported_not_zero(self):
        c = config()
        c["lanes"][0].pop("counting_line")
        m = Measurements(c)
        m.update([], 0)
        self.assertIsNone(m.update([], 1)[3][0]["crossed_vehicle_count"])

    def test_invalid_calibration_and_nonmonotonic_time_rejected(self):
        c = config()
        c["lanes"][0]["counting_line"] = [[0.5, 0.5], [0.5, 0.5]]
        with self.assertRaises(ValueError):
            Measurements(c)
        m = Measurements(config())
        m.update([], 1)
        with self.assertRaises(ValueError):
            m.update([], 1)

    def test_overload_is_bounded_and_reported_partial(self):
        m = Measurements(config(maximum_detections=1))
        m.update([car(), car(x=0.1)], 0)
        window = m.update([car(), car(x=0.1)], 1)
        self.assertEqual(window[3][0]["visible_vehicle_count"], 1)
        self.assertEqual(window[2], "partial")


if __name__ == "__main__":
    unittest.main()

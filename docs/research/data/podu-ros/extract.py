"""Extract selected complete-car track summaries from authors' tabular release.

Requires pyarrow. Does not load or execute pickles or authors' code.
Usage: python extract.py RAW.parquet IDS.txt OUTPUT_DIRECTORY
"""

import collections
import csv
import hashlib
import json
import pathlib
import sys

import pyarrow.parquet as pq


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


raw, id_file, output = map(pathlib.Path, sys.argv[1:])
output.mkdir(parents=True, exist_ok=True)
ids = set(map(int, id_file.read_text().split()))
table = pq.read_table(raw)
columns = {name: table[name].to_pylist() for name in table.column_names}
times = columns['kafka_timestamp']
minimum, maximum = min(times), max(times)
span_seconds = (maximum - minimum) / 1000
first, last, classes = {}, {}, collections.defaultdict(set)
for index, track_id in enumerate(columns['tracker_id']):
    if track_id not in ids:
        continue
    classes[track_id].add(columns['class_id'][index])
    if track_id not in first or times[index] < times[first[track_id]]:
        first[track_id] = index
    if track_id not in last or times[index] > times[last[track_id]]:
        last[track_id] = index
assert set(first) == ids
assert all(classes[x] == {2} for x in ids), 'Selected list must contain only car tracks'
assert all(times[first[x]] < maximum for x in ids)

with (output / 'selected-track-endpoints.csv').open('w', newline='') as stream:
    writer = csv.writer(stream)
    writer.writerow(['tracker_id', 'first_elapsed_seconds', 'last_elapsed_seconds',
                     'first_image_x', 'first_image_y', 'last_image_x', 'last_image_y'])
    for track_id in sorted(ids):
        endpoints = []
        for index in (first[track_id], last[track_id]):
            # Authors' add_centroid_xy_v2 with [.5, .1]: 50% width,
            # 10% height up from the bottom (90% down from the top).
            endpoints.extend([(columns['left'][index] + columns['right'][index]) / 2,
                              columns['top'][index] * 0.1 +
                              columns['bottom'][index] * 0.9])
        writer.writerow([track_id, (times[first[track_id]] - minimum) / 1000,
                         (times[last[track_id]] - minimum) / 1000, *endpoints])

mean_sample_per_second = len(ids) / span_seconds
bins = []
for start in range(0, int(span_seconds) + 1, 900):
    end = min(start + 900, span_seconds)
    count = sum(start <= (times[first[x]] - minimum) / 1000 < end for x in ids)
    bins.append({'start_elapsed_seconds': start, 'end_elapsed_seconds': end,
                 'complete_car_track_starts': count,
                 'relative_weight': count / (end - start) / mean_sample_per_second,
                 'quality': 'observed_selected_tracks_processing_clock'})
assert sum(x['complete_car_track_starts'] for x in bins) == len(ids)
with (output / 'observed-track-starts.csv').open('w', newline='') as stream:
    writer = csv.DictWriter(stream, fieldnames=bins[0].keys())
    writer.writeheader()
    writer.writerows(bins)

unique_times = sorted(set(times))
cadence = collections.Counter(b - a for a, b in zip(unique_times, unique_times[1:]))
summary = {
    'format_version': 1,
    'raw_sha256': digest(raw), 'raw_size_bytes': raw.stat().st_size,
    'selected_id_sha256': digest(id_file), 'selected_id_size_bytes': id_file.stat().st_size,
    'raw_rows': table.num_rows, 'selected_complete_car_tracks': len(ids),
    'raw_first_kafka_timestamp_ms': minimum, 'raw_last_kafka_timestamp_ms': maximum,
    'raw_constant_start_timestamp_ms': sorted(set(columns['start_timestamp'])),
    'processing_clock_span_seconds': span_seconds, 'unique_frame_timestamps': len(unique_times),
    'most_common_frame_timestamp_gaps_ms': cadence.most_common(5),
    'reported_recording_date': '2024-07-16', 'reported_start_time': '19:09',
    'reported_end_time': '21:55', 'reported_duration_minutes': 166,
    'paper_sample_throughput_reference_per_hour': len(ids) / 166 * 60,
    'processing_clock_sample_throughput_per_hour': mean_sample_per_second * 3600,
    'recording_time_mapping_verified': False,
    'notes': 'Count each selected ID once at first kafka_timestamp. Raw records are image detections, '
             'not measured SUMO edge flows. Processing clock differs from capture date. Mapping '
             '19:09 + elapsed to recording time is an approximation; outside-window values are estimates. '
             'The sample excludes incomplete car tracks and other classes and covers one camera view.'
}
(output / 'extraction-summary.json').write_text(json.dumps(summary, indent=2) + '\n')
print(json.dumps(summary, indent=2))

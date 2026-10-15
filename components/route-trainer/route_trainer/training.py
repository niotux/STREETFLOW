import copy
import hashlib
import json
import math
import random
import time
from pathlib import Path

import numpy as np
import onnxruntime as ort
import torch

from .dataset import validate
from .model import RouteModel


def train(network, dataset, output, epochs=200, seed=42, progress=None):
    if type(epochs) is not int or not 1 <= epochs <= 5000:
        raise ValueError("epochs must be an integer from 1 to 5000")
    if len(network.edge_ids) > 100000 or network.max_turns > 128:
        raise ValueError("Map exceeds the v3 limits of 100000 roads or 128 choices per road")
    if type(seed) is not int or not 0 <= seed <= 2147483647:
        raise ValueError("seed must be an integer from 0 to 2147483647")
    torch.set_num_threads(1)
    torch.manual_seed(seed)
    random.seed(seed)
    rows, periods = validate(dataset, network)
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    road_indices = {edge: index for index, edge in enumerate(network.edge_ids)}
    exits = sorted({row["path"][-1] for row in rows})
    maximum_rate = max(row["vehicles_per_hour"] for row in rows)
    examples = []
    for period_index, period in enumerate(periods):
        for row in rows:
            if row["start_seconds"] <= period["start_seconds"] < row["end_seconds"]:
                weight = row["vehicles_per_hour"] / maximum_rate * ((period["end_seconds"] - period["start_seconds"]) / 86400)
                if not np.isfinite(np.float32(weight)) or np.float32(weight) <= 0:
                    raise ValueError("Route influence ratios exceed the supported training numeric range")
                examples.append({"period": period_index, "row": row, "weight": weight})
    model = RouteModel(len(road_indices), len(periods), network.max_turns)
    optimizer = torch.optim.Adam(model.parameters(), lr=0.005)
    best_loss, best_state, stale = math.inf, None, 0
    started = time.perf_counter()
    for epoch in range(epochs):
        # Full weighted objective, accumulated in bounded batches. No invented routes.
        optimizer.zero_grad()
        total_weight = sum(example["weight"] for example in examples)
        epoch_loss = 0.0
        for offset in range(0, len(examples), 64):
            batch = examples[offset:offset + 64]
            loss = batch_loss(model, batch, network, road_indices, exits) / total_weight
            loss.backward()
            epoch_loss += float(loss.detach())
        if not math.isfinite(epoch_loss):
            raise ValueError("Training produced a nonfinite loss; inspect dataset rates and route lengths")
        if epoch_loss < best_loss - 0.00001:
            best_loss, best_state, stale = epoch_loss, copy.deepcopy(model.state_dict()), 0
        else:
            stale += 1
        torch.nn.utils.clip_grad_norm_(model.parameters(), 5)
        optimizer.step()
        if progress:
            progress({"epoch": epoch + 1, "epochs": epochs, "training_loss": epoch_loss})
        if stale >= 30:
            break
    model.load_state_dict(best_state)
    model.eval()
    example_inputs = (torch.tensor([0]), torch.tensor([network.max_turns + 1]), torch.tensor([0]), torch.zeros(1, model.hidden_size))
    model_path = output / "route-model.onnx"
    # Export a single recurrent step: route length is controlled by the caller.
    torch.onnx.export(model, example_inputs, str(model_path), input_names=["road", "previous_turn", "period", "hidden"], output_names=["logits", "next_hidden"], opset_version=18, dynamo=True, external_data=False)
    session_options = ort.SessionOptions()
    session_options.intra_op_num_threads = 1
    session_options.inter_op_num_threads = 1
    session = ort.InferenceSession(str(model_path), sess_options=session_options, providers=["CPUExecutionProvider"])
    inputs = {name: value.numpy() for name, value in zip(("road", "previous_turn", "period", "hidden"), example_inputs)}
    actual = session.run(None, inputs)
    with torch.no_grad():
        expected = model(*example_inputs)
    for left, right in zip(actual, expected):
        if not np.isfinite(left).all() or not torch.isfinite(right).all():
            raise ValueError("Model produced nonfinite values during export verification")
        np.testing.assert_allclose(left, right.numpy(), atol=0.00001, rtol=0.0001)
    max_movements = min(10000, max(64, max(len(row["turns"]) for row in rows) * 4))
    model_document = {
        "format_version": 3, "architecture": "local_turn_gru_v1", "model_file": "route-model.onnx",
        "network_sha256": network.sha256, "dataset_sha256": hashlib.sha256(json.dumps(dataset, sort_keys=True, separators=(",", ":")).encode()).hexdigest(),
        "vehicle_type": {"id": "car", "vehicle_class": network.vehicle_class, "weight": 1},
        "repeat_daily": dataset["repeat_daily"], "hidden_size": model.hidden_size,
        "max_turns": network.max_turns, "maximum_route_movements": max_movements,
        "roads": [{"edge_id": edge, "next_edge_ids": network.edges[edge]["next_edge_ids"], "can_exit": edge in exits} for edge in network.edge_ids],
        "periods": periods,
    }
    report = evaluate(session, model_document, network, seed)
    authored_shares = {}
    baseline_counts = {}
    total_weight = sum(example["weight"] for example in examples)
    for example in examples:
        period_index, row = example["period"], example["row"]
        origin_rate = sum(arrival["vehicles_per_hour"] for arrival in periods[period_index]["arrivals"] if arrival["start_edge"] == row["start_edge"])
        key = f"period={period_index}:" + " > ".join(row["path"])
        authored_shares[key] = authored_shares.get(key, 0) + row["vehicles_per_hour"] / origin_rate
        for step, edge in enumerate(row["path"]):
            target = row["turns"][step] if step < len(row["turns"]) else network.max_turns
            counts = baseline_counts.setdefault((period_index, edge), {})
            counts[target] = counts.get(target, 0) + example["weight"]
    baseline_loss = 0
    for counts in baseline_counts.values():
        total = sum(counts.values())
        for count in counts.values():
            baseline_loss -= count * math.log(count / total) / total_weight
    report["authored_route_shares_within_origin_and_period"] = authored_shares
    report["memoryless_baseline_training_loss"] = baseline_loss
    report.update({"seed": seed, "epochs_completed": epoch + 1, "parameter_count": sum(value.numel() for value in model.parameters()), "training_seconds": time.perf_counter() - started, "training_loss": best_loss,
                   "evaluation_kind": "fit_to_authored_templates", "note": "No independent observed test dataset was supplied. Generated samples measure fit and termination, not real-world accuracy."})
    (output / "neural-model.json").write_text(json.dumps(model_document, indent=2))
    (output / "training-report.json").write_text(json.dumps(report, indent=2))
    (output / "route-dataset.json").write_text(json.dumps(dataset, indent=2))
    return model_document, report


def batch_loss(model, examples, network, road_indices, exits):
    hidden = torch.zeros(len(examples), model.hidden_size)
    periods = torch.tensor([example["period"] for example in examples])
    weights = torch.tensor([example["weight"] for example in examples], dtype=torch.float32)
    previous = torch.full((len(examples),), network.max_turns + 1)
    possible_lanes = [network.initial_lanes(example["row"]["start_edge"]) for example in examples]
    loss = torch.tensor(0.0)
    for step in range(max(len(example["row"]["path"]) for example in examples)):
        roads, targets, masks = [], [], []
        for index, example in enumerate(examples):
            path = example["row"]["path"]
            if step >= len(path):
                roads.append(0)
                targets.append(-100)
                masks.append([True] * (network.max_turns + 1))
                continue
            edge = path[step]
            roads.append(road_indices[edge])
            target = example["row"]["turns"][step] if step < len(path) - 1 else network.max_turns
            targets.append(target)
            mask = [False] * (network.max_turns + 1)
            for turn, next_edge in enumerate(network.edges[edge]["next_edge_ids"]):
                mask[turn] = bool(network.advance_lanes(edge, next_edge, possible_lanes[index]))
            mask[network.max_turns] = edge in exits
            masks.append(mask)
            if target != network.max_turns:
                possible_lanes[index] = network.advance_lanes(edge, path[step + 1], possible_lanes[index])
        logits, hidden = model(torch.tensor(roads), previous, periods, hidden)
        logits = logits.masked_fill(~torch.tensor(masks), -1e9)
        target_tensor = torch.tensor(targets)
        losses = torch.nn.functional.cross_entropy(logits, target_tensor, reduction="none", ignore_index=-100)
        loss = loss + (losses * weights).sum()
        previous = torch.clamp(target_tensor, min=0)
    return loss


def evaluate(session, document, network, seed):
    random_state = np.random.default_rng(seed)
    indices = {road["edge_id"]: index for index, road in enumerate(document["roads"])}
    steps = failures = generated = 0
    paths = {}
    started = time.perf_counter()
    for period_index, period in enumerate(document["periods"]):
        for arrival in period["arrivals"]:
            for _ in range(32):
                generated += 1
                edge = arrival["start_edge"]
                path = [edge]
                lanes = network.initial_lanes(edge)
                previous, hidden = document["max_turns"] + 1, np.zeros((1, document["hidden_size"]), dtype=np.float32)
                for step in range(document["maximum_route_movements"] + 1):
                    road = document["roads"][indices[edge]]
                    logits, hidden = session.run(None, {"road": np.array([indices[edge]], dtype=np.int64), "previous_turn": np.array([previous], dtype=np.int64), "period": np.array([period_index], dtype=np.int64), "hidden": hidden})
                    steps += 1
                    legal = [turn for turn, following in enumerate(road["next_edge_ids"]) if network.advance_lanes(edge, following, lanes)]
                    if road["can_exit"]:
                        legal.append(document["max_turns"])
                    if not legal:
                        failures += 1
                        break
                    scores = logits[0, legal].astype(np.float64)
                    probabilities = np.exp(scores - scores.max())
                    probabilities /= probabilities.sum()
                    choice = int(random_state.choice(legal, p=probabilities))
                    if choice == document["max_turns"]:
                        key = f"period={period_index}:" + " > ".join(path)
                        paths[key] = paths.get(key, 0) + 1
                        break
                    if step == document["maximum_route_movements"]:
                        failures += 1
                        break
                    following = road["next_edge_ids"][choice]
                    lanes = network.advance_lanes(edge, following, lanes)
                    edge, previous = following, choice
                    path.append(edge)
    elapsed = time.perf_counter() - started
    return {"generated_routes": generated, "failed_routes": failures, "sampled_paths": paths, "cpu_generation_seconds": elapsed, "routes_per_second": generated / max(elapsed, 1e-9), "inference_steps": steps}

import math

DAY_SECONDS = 86400


def validate(dataset, network, allow_empty=False):
    if not isinstance(dataset, dict):
        raise ValueError("Dataset must be a JSON object")
    allowed = {"format_version", "network_sha256", "vehicle_class", "repeat_daily", "routes"}
    if set(dataset) - allowed:
        raise ValueError(f"Unknown dataset fields: {sorted(set(dataset) - allowed)}")
    if dataset.get("format_version") != 1:
        raise ValueError("Dataset format_version must be 1")
    if dataset.get("network_sha256") != network.sha256:
        raise ValueError("Dataset belongs to a different network revision")
    if dataset.get("vehicle_class") != network.vehicle_class:
        raise ValueError("Dataset vehicle_class differs from the loaded map")
    if not isinstance(dataset.get("repeat_daily"), bool):
        raise ValueError("repeat_daily must be a boolean")
    rows = dataset.get("routes")
    if not isinstance(rows, list) or (not rows and not allow_empty):
        raise ValueError("Author or import at least one route")
    ids, prepared = set(), []
    boundaries = {0, DAY_SECONDS}
    for row in rows:
        if not isinstance(row, dict):
            raise ValueError("Each route must be an object")
        allowed_route_fields = {"id", "start_seconds", "end_seconds", "start_edge", "turns", "vehicles_per_hour"}
        if set(row) - allowed_route_fields:
            raise ValueError(f"Unknown route fields: {sorted(set(row) - allowed_route_fields)}")
        identifier = row.get("id")
        if not isinstance(identifier, str) or not identifier.strip() or identifier in ids:
            raise ValueError("Each route needs a unique nonempty id")
        ids.add(identifier)
        start, end = row.get("start_seconds"), row.get("end_seconds")
        if type(start) is not int or type(end) is not int or not 0 <= start < end <= DAY_SECONDS:
            raise ValueError(f"{identifier}: span must use integer seconds with 0 <= start < end <= 86400; split overnight spans")
        rate = row.get("vehicles_per_hour")
        if isinstance(rate, bool) or not isinstance(rate, (int, float)) or not math.isfinite(rate) or rate <= 0:
            raise ValueError(f"{identifier}: vehicles_per_hour must be finite and positive")
        turns = row.get("turns")
        if not isinstance(turns, list) or len(turns) > 10000:
            raise ValueError(f"{identifier}: turns must be an array with at most 10000 movements")
        try:
            path = network.replay(row.get("start_edge"), turns)
        except ValueError as error:
            raise ValueError(f"{identifier}: {error}") from error
        prepared.append({**row, "path": path})
        boundaries.update((start, end))
    boundaries = sorted(boundaries)
    if len(boundaries) > 1441:
        raise ValueError("Too many time boundaries; use at most 1440 daily periods")
    periods = []
    for start, end in zip(boundaries, boundaries[1:]):
        arrivals = {}
        for row in prepared:
            if row["start_seconds"] <= start < row["end_seconds"]:
                origin = row["start_edge"]
                arrivals[origin] = arrivals.get(origin, 0) + row["vehicles_per_hour"]
                if not math.isfinite(arrivals[origin]):
                    raise ValueError("Summed arrival rates exceed numeric range")
        periods.append({"start_seconds": start, "end_seconds": end, "arrivals": [
            {"start_edge": origin, "vehicles_per_hour": rate} for origin, rate in sorted(arrivals.items())
        ]})
        if not math.isfinite(sum(arrivals.values())):
            raise ValueError("Total arrival rate exceeds numeric range")
    return prepared, periods


def blank(network):
    return {"format_version": 1, "network_sha256": network.sha256, "vehicle_class": network.vehicle_class, "repeat_daily": True, "routes": []}

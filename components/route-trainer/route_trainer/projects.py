import json
import shutil
import stat
import zipfile
from pathlib import Path, PurePosixPath

from .network import Network

MAXIMUM_PROJECT_BYTES = 256 * 1024 * 1024
MAXIMUM_PROJECT_FILES = 4096


def contained(root, relative):
    name = PurePosixPath(relative.replace("\\", "/"))
    if name.is_absolute() or ".." in name.parts or ":" in relative:
        raise ValueError("Project references must stay inside the package")
    resolved = (Path(root) / str(name)).resolve()
    if not resolved.is_relative_to(Path(root).resolve()):
        raise ValueError("Project reference leaves package")
    return resolved


def unpack(archive_path, destination):
    destination = Path(destination)
    destination.mkdir(parents=True, exist_ok=True)
    total, seen = 0, set()
    with zipfile.ZipFile(archive_path) as archive:
        if len(archive.infolist()) > MAXIMUM_PROJECT_FILES:
            raise ValueError("Too many project files")
        for item in archive.infolist():
            kind = stat.S_IFMT(item.external_attr >> 16)
            if kind not in (0, stat.S_IFREG, stat.S_IFDIR):
                raise ValueError("ZIP links and special files are unsupported")
            normalized = item.filename.replace("\\", "/").rstrip("/")
            if normalized.casefold() in seen:
                raise ValueError("Duplicate ZIP path")
            seen.add(normalized.casefold())
            target = contained(destination, normalized)
            if item.is_dir():
                target.mkdir(parents=True, exist_ok=True)
                continue
            total += item.file_size
            if total > MAXIMUM_PROJECT_BYTES:
                raise ValueError("Project is too large")
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(item) as source, target.open("xb") as output:
                shutil.copyfileobj(source, output)


def load_project(directory, vehicle_class=None):
    directory = Path(directory)
    manifests = list(directory.rglob("experiment.json"))
    if len(manifests) != 1:
        raise ValueError("Project must contain exactly one experiment.json")
    root = manifests[0].parent
    manifest = json.loads(manifests[0].read_text())
    if manifest.get("format_version") not in (1, 2, 3):
        raise ValueError("Supported project versions: 1, 2, 3")
    if vehicle_class is None:
        vehicle_class = "passenger"
        if manifest.get("demand_file"):
            demand = json.loads(contained(root, manifest["demand_file"]).read_text())
            for vehicle in demand.get("vehicle_types", []):
                if vehicle.get("weight", 0) > 0:
                    vehicle_class = vehicle["vehicle_class"]
                    break
        elif manifest.get("journey_model_file"):
            model = json.loads(contained(root, manifest["journey_model_file"]).read_text())
            vehicle_class = model["axes"]["vehicle_type"][0]["vehicle_class"]
        elif manifest.get("neural_model_file"):
            model = json.loads(contained(root, manifest["neural_model_file"]).read_text())
            vehicle_class = model["vehicle_type"]["vehicle_class"]
    network = Network(contained(root, manifest["network"]["file"]), vehicle_class)
    return root, manifest, network


def export_project(source, trained, destination, project_id, project_name):
    if not project_id or len(project_id) > 128 or any(value.isspace() or ord(value) < 32 for value in project_id):
        raise ValueError("Project ID needs 1..128 non-whitespace characters")
    if not project_name.strip():
        raise ValueError("Project name is required")
    destination = Path(destination)
    destination.mkdir(parents=True)
    original = json.loads((Path(source) / "experiment.json").read_text())
    assets = [original["network"]["file"], *original.get("additional_files", [])]
    for field in ("terminals_file", "detectors_file", "traffic_lights_file"):
        if original.get(field):
            assets.append(original[field])
    generated_names = {item.name.casefold() for item in Path(trained).iterdir()}
    for asset in assets:
        if asset.replace("\\", "/").casefold() in generated_names:
            raise ValueError(f"Source asset collides with a generated model file: {asset}")
    for asset in assets:
        target = contained(destination, asset)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(contained(source, asset), target)
    for item in Path(trained).iterdir():
        shutil.copy2(item, destination / item.name)
    manifest = original
    manifest.pop("demand_file", None)
    manifest.pop("journey_model_file", None)
    manifest.update({"format_version": 3, "id": project_id, "name": project_name, "neural_model_file": "neural-model.json"})
    manifest["description"] = "Time-dependent neural routes trained from an authored route dataset."
    (destination / "experiment.json").write_text(json.dumps(manifest, indent=2))
    return destination

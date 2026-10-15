import json
import os
import shutil
import threading
import uuid
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from fastapi import FastAPI, File, HTTPException, UploadFile
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles

from .dataset import blank, validate
from .projects import MAXIMUM_PROJECT_BYTES, export_project, load_project, unpack

app = FastAPI(title="Streetflow Route Trainer")
root = Path(os.environ.get("TRAINER_DATA_ROOT", ".data/route-trainer")).resolve()
root.mkdir(parents=True, exist_ok=True)
executor = ThreadPoolExecutor(max_workers=1)
training_lock = threading.Lock()
jobs = {}


def workspace(identifier):
    try:
        if str(uuid.UUID(identifier)) != identifier:
            raise ValueError("Invalid workspace ID")
    except ValueError as error:
        raise HTTPException(404, "Unknown workspace") from error
    path = root / identifier
    if not path.is_dir():
        raise HTTPException(404, "Unknown workspace")
    return path


@app.exception_handler(ValueError)
async def invalid_request(request, exception):
    from fastapi.responses import JSONResponse
    return JSONResponse({"detail": str(exception)}, status_code=400)


@app.post("/api/projects")
async def import_project(file: UploadFile = File(...)):
    identifier = str(uuid.uuid4())
    path = root / identifier
    path.mkdir()
    try:
        upload = path / "upload"
        total = 0
        with upload.open("wb") as stream:
            while chunk := await file.read(1024 * 1024):
                total += len(chunk)
                if total > MAXIMUM_PROJECT_BYTES:
                    raise ValueError("Upload exceeds 256 MiB")
                stream.write(chunk)
        contents = path / "source"
        if (file.filename or "").lower().endswith(".zip"):
            unpack(upload, contents)
        else:
            contents.mkdir()
            shutil.move(upload, contents / "network.net.xml")
            (contents / "experiment.json").write_text(json.dumps({"format_version": 3, "id": "map", "name": "Imported map", "network": {"file": "network.net.xml"}, "defaults": {"start_time_seconds": 25200, "step_seconds": 0.5}}))
        source, manifest, network = load_project(contents)
        (path / "source-root.txt").write_text(str(source))
        dataset_file = source / "route-dataset.json"
        dataset = json.loads(dataset_file.read_text()) if dataset_file.exists() else blank(network)
        if dataset["routes"]:
            validate(dataset, network)
        (path / "dataset.json").write_text(json.dumps(dataset, indent=2))
        return {"id": identifier, "manifest": manifest, "network": network.view(), "dataset": dataset}
    except Exception:
        shutil.rmtree(path)
        raise


def project_for(identifier):
    path = workspace(identifier)
    source = Path((path / "source-root.txt").read_text())
    return path, source, load_project(source)[2]


@app.get("/api/projects/{identifier}")
def read_project(identifier: str):
    path, source, network = project_for(identifier)
    manifest = json.loads((source / "experiment.json").read_text())
    return {"id": identifier, "manifest": manifest, "network": network.view(), "dataset": json.loads((path / "dataset.json").read_text())}


@app.put("/api/projects/{identifier}/dataset")
def save_dataset(identifier: str, dataset: dict):
    path, source, network = project_for(identifier)
    rows, periods = validate(dataset, network, allow_empty=True)
    temporary = path / "dataset.tmp"
    temporary.write_text(json.dumps(dataset, indent=2))
    temporary.replace(path / "dataset.json")
    return {"routes": len(rows), "periods": periods, "paths": [{"id": row["id"], "edges": row["path"]} for row in rows]}


@app.post("/api/projects/{identifier}/train")
def start_training(identifier: str, settings: dict):
    path, source, network = project_for(identifier)
    dataset = json.loads((path / "dataset.json").read_text())
    validate(dataset, network)
    epochs, seed = settings.get("epochs", 200), settings.get("seed", 42)
    if type(epochs) is not int or not 1 <= epochs <= 5000 or type(seed) is not int or not 0 <= seed <= 2147483647:
        raise ValueError("Use epochs 1..5000 and seed 0..2147483647")
    project_id = settings.get("project_id", "")
    project_name = settings.get("project_name", "")
    if not project_id or not project_name:
        raise ValueError("Export project ID and name are required")
    if not training_lock.acquire(blocking=False):
        raise HTTPException(409, "Another training job is running")
    job_id = str(uuid.uuid4())
    jobs[job_id] = {"state": "queued", "workspace_id": identifier}

    def run():
        try:
            from .training import train
            jobs[job_id] = {"state": "training", "workspace_id": identifier}

            def progress(value):
                jobs[job_id] = {"state": "training", "workspace_id": identifier, **value}

            trained = path / "jobs" / job_id / "trained"
            model, report = train(network, dataset, trained, epochs, seed, progress)
            package = trained.parent / "package"
            export_project(source, trained, package, project_id, project_name)
            archive = shutil.make_archive(str(trained.parent / "project"), "zip", package)
            jobs[job_id] = {"state": "complete", "workspace_id": identifier, "report": report, "download": f"/api/jobs/{job_id}/project.zip", "archive": archive}
        except Exception as error:
            jobs[job_id] = {"state": "failed", "workspace_id": identifier, "error": str(error)}
        finally:
            training_lock.release()

    executor.submit(run)
    return {"job_id": job_id}


@app.get("/api/jobs/{identifier}")
def job_status(identifier: str):
    job = jobs.get(identifier)
    if job is None:
        raise HTTPException(404, "Unknown job")
    return {key: value for key, value in job.items() if key != "archive"}


@app.get("/api/jobs/{identifier}/project.zip")
def download_project(identifier: str):
    job = jobs.get(identifier)
    if job is None or job["state"] != "complete":
        raise HTTPException(404, "No completed model package")
    return FileResponse(job["archive"], media_type="application/zip", filename="neural-project.zip")


shared_ui = Path(os.environ.get("STREETFLOW_UI_ROOT", Path(__file__).resolve().parents[2] / "ui" / "web"))
app.mount("/ui", StaticFiles(directory=shared_ui), name="shared-ui")
app.mount("/", StaticFiles(directory=Path(__file__).parent.parent / "web", html=True), name="web")

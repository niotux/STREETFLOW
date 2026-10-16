# Projects folder

Projects in all supported formats (Edge Population v1, Transformer Routes v2, and Neural Routes v3) live in one folder on the machine running Streetflow. The simulator lists everything in it: each project folder and each project `.zip`. Listing a project does not create a run or start SUMO. No database or registration step is involved.

## Location

Docker Compose mounts the repository's `projects/` folder read-only at `/projects` (`Simulator__ProjectsRoot`). Everything in `projects/` except its README is git-ignored; copy the bundled examples to start:

```sh
cp -r experiments/drafts/* projects/
```

To use another folder, set `STREETFLOW_PROJECTS_DIR=/absolute/path` in the repository's `.env` and run `docker compose up -d simulator`. Only the simulator container is recreated (any loaded simulation stops); no rebuild is needed. The mount is fixed while the container runs, so the folder cannot be changed from the UI. Native execution reads `projects/` at the repository root unless `Simulator__ProjectsRoot` names another folder.

## What is listed

| Entry | Listed as |
| --- | --- |
| Folder with `experiment.json` at its root | A project, read in place |
| `.zip` with exactly one `experiment.json` (flat, or inside a single folder) | A project, extracted once into `Simulator__DataRoot/project-cache/` |
| Folder without `experiment.json`, invalid project, unreadable ZIP | Unavailable, with the reason |
| Second entry with an already listed project ID (in name order) | Unavailable: duplicate project ID |
| Names starting with `.`, and other files (such as the README) | Ignored |

The folder is read on every list and project request, so edits, additions and removals appear after **Refresh list** in Loader. A ZIP's cache entry is keyed by its name, size and modification time: replacing the ZIP extracts it again, and removing it deletes the cached copy. `Simulator__MaximumProjectBytes` (default 256 MiB) and `Simulator__MaximumProjectFiles` (default 4096) bound each ZIP.

## Loading and unloading

In Balthasar's Loader, choose a project, set the seed and start time, and click **Load**. Loading creates a paused simulation; Runner starts and controls it. Loading while a simulation is running stops it and loads the new project paused, so you can switch projects at any time. **Unload** stops the simulation and leaves nothing loaded: the map is empty and Runner is disabled. Stop in Runner ends the traffic but keeps the project loaded, so **Reset run** can replay it.

Each run saves its input snapshot under `DataRoot/city-runs/{id}/package/`. Editing or removing the project afterwards does not change a loaded run; restart replays the saved snapshot, and the next Load reads the current files.

## API

```sh
curl http://localhost:8080/api/v1/projects/sources
curl 'http://localhost:8080/api/v1/projects/tiny-graph/map?limit=2'
curl -H 'Content-Type: application/json' --data '{"seed":42}' http://localhost:8080/api/v1/projects/tiny-graph/runs
curl http://localhost:8080/api/v1/city-runs/loaded
curl -X DELETE http://localhost:8080/api/v1/city-runs/loaded
```

| Request | Result |
| --- | --- |
| `GET /api/v1/projects/sources` | Every listed entry: `{name, kind, project, error}`, with `kind` `folder` or `zip`; unavailable entries have a null `project` |
| `GET /api/v1/projects` | Readable, validated projects, sorted by ID |
| `GET /api/v1/projects/{id}` | Project manifest, counts, revision, and folder path |
| `GET /api/v1/projects/{id}/map?offset=0&limit=1000` | Geometry and physical devices; limits 1–10000 |
| `GET /api/v1/projects/{id}/demand` | Demand/model specification |
| `POST /api/v1/projects/{id}/runs` | Load the project; replaces any loaded simulation |
| `GET /api/v1/city-runs/loaded` | The loaded simulation (running, paused or stopped), or HTTP 204 when none is loaded |
| `DELETE /api/v1/city-runs/loaded` | Unload: stop any traffic and leave no simulation loaded; HTTP 204 |

Unknown projects return 404. A service restart leaves no simulation loaded.

## Validation

Format-specific validation applies to every entry: required files, JSON/XML structure, contained relative references, lane/controller bindings, traffic settings, and neural model checks. Relative manifest references may not escape the project folder. Symbolic links, unsafe ZIP paths, non-regular ZIP entries and case-colliding ZIP paths are rejected.

Tests cover all formats, in-place edits and revisions, restart persistence, unavailable and ignored entries, duplicate identities, ZIP caching and limits, and load/unload behavior.

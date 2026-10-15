# Project folder loading

Projects in all supported formats (Edge Population v1, Transformer Routes v2, and Neural Routes v3) live in ordinary folders. The simulator keeps a persistent list of absolute folder paths and reads inputs from those folders. Registering a folder does not copy it, create a run, or start SUMO. No database is involved.

## UI

In Balthasar's Loader, enter the folder containing `experiment.json` and click **Add folder**. Select its project to preview the map, then **Load simulation** to create a paused run. Edit the same files using any external tools and click **Refresh from disk** to update the preview. New runs read the latest validated files.

**Manage project folders** shows every saved path and any validation/read error. **Remove from list** removes only that reference. Files and saved runs remain intact. Missing or temporarily invalid folders stay registered, do not prevent service startup, and become usable again after their files are repaired and refreshed.

A path must be absolute and visible to the simulator process. Compose mounts the host `experiments/drafts/` directory at `/app/projects/`; add other bind mounts for folders elsewhere. Experiments are not bundled into the runtime image. With native execution, use the folder's normal local path.

## API

```sh
curl http://localhost:8080/api/v1/projects/folders \
  -H 'Content-Type: application/json' \
  --data '{"path":"/app/projects/tiny-graph"}'
curl http://localhost:8080/api/v1/projects
curl 'http://localhost:8080/api/v1/projects/tiny-graph/map?limit=2'
curl -X DELETE http://localhost:8080/api/v1/projects/folders \
  -H 'Content-Type: application/json' \
  --data '{"path":"/app/projects/tiny-graph"}'
```

| Request | Result |
| --- | --- |
| `POST /api/v1/projects/folders` | Register `{ "path": "absolute/folder/path" }`; return project info and its `path` |
| `GET /api/v1/projects/folders` | All registered entries: `{path, project, error}`; unavailable folders have null `project` and an explanation |
| `DELETE /api/v1/projects/folders` | Remove the path supplied in the JSON body; HTTP 204; files are preserved |
| `GET /api/v1/projects` | Currently readable, validated projects, sorted by ID |
| `GET /api/v1/projects/{id}` | Read current project manifest, counts, revision, and folder path |
| `GET /api/v1/projects/{id}/map?offset=0&limit=1000` | Current geometry and physical devices; limits 1–10000 |
| `GET /api/v1/projects/{id}/demand` | Current demand/model specification |
| `POST /api/v1/projects/import/folder` | Compatibility alias for folder registration; it no longer copies the folder |
| `POST /api/v1/projects/import/zip` | Compatibility upload: extract a ZIP into an ordinary persistent folder, validate and register its path |

Re-registering the same normalized path is harmless. Different registered folders with the same manifest ID return HTTP 409 `duplicate_project`. If an external edit creates a duplicate ID, the later entry is reported unavailable. Unknown projects/folders return 404. Validation errors return `{code, message}` with HTTP 400. Removing an unreadable folder works using its path.

## Disk persistence and edits

`Simulator__DataRoot/project-folders.json` contains the registry:

```json
[
  {"path": "/app/projects/tiny-graph"},
  {"path": "/app/projects/four-way-neural-routes"}
]
```

Registry updates replace this file atomically. API changes take effect immediately; manual registry-file edits take effect after a service restart. Project-file edits are read on each project request; no folder watcher or database synchronization is needed. Save a complete valid project before refreshing or loading a new run. The revision is recalculated from current referenced inputs.

Existing private imports under `DataRoot/projects/*/package/` are added as folder references the first time the registry is created. Their inputs and old metadata are preserved. They can be removed from the list and replaced with paths to the original source folders. Once the registry exists, removed legacy entries are not rediscovered automatically.

New runs still save their own input snapshots under `DataRoot/city-runs/{id}/package/`. Editing or unregistering the source does not change an already loaded run. Restart replays the saved snapshot; loading a new simulation uses the current folder. Run snapshots are separate from the editable project registry.

## Validation and uploads

The existing format-specific validation remains in place: required files, JSON/XML structure, contained relative references, lane/controller bindings, traffic settings, and neural model checks are validated before use. Relative manifest references may not escape the project folder. Unsupported symbolic links remain rejected.

ZIP compatibility uploads accept flat or single-folder archives containing exactly one `experiment.json`. `Simulator__MaximumProjectBytes` (default 256 MiB) and `Simulator__MaximumProjectFiles` (default 4096) bound uploads and expansion. Folder references do not copy or enumerate unrelated project files, so these upload limits do not restrict folder registration. The returned path identifies the extracted folder for later edits or removal from the list.

Tests cover all formats, disk edits and revisions, restart persistence, invalid/missing folders, non-destructive removal, old-import migration, duplicate identities, input validation, and archive limits.

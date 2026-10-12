# City project loading

Task 3 adds backend import, storage, validation, and map inspection for the [accepted format](experiment-project-format.md). Importing does not create a run or start SUMO. Project selection in the browser and continuous traffic generation are later tasks.

## Manual review

Run these commands from the repository root with .NET 10 installed. Import/map inspection does not require a local SUMO executable.

```sh
dotnet run --project components/simulator/Streetflow.Simulator --no-launch-profile --urls http://localhost:8080
```

In another terminal, import Podu Roș using an absolute path visible to the service:

```sh
curl -i http://localhost:8080/api/v1/projects/import/folder \
  -H 'Content-Type: application/json' \
  --data '{"path":"/Space/CodeDev/projects/hackaton/STREETFLOW/experiments/drafts/podu-ros"}'
curl http://localhost:8080/api/v1/projects
curl http://localhost:8080/api/v1/projects/podu-ros/map?limit=2
curl http://localhost:8080/api/v1/projects/podu-ros/demand
curl http://localhost:8080/api/v1/runs
```

The import returns HTTP 201 with 106 usable edges and a SHA-256 input revision. The map returns lane shapes, ordinary edge identifiers, junctions, signal identifiers, and attribution in SUMO local metres. Demand returns the explicit profiles and generation settings. Importing adds no run. Restart the service and list projects again to confirm persistence. Import the same ID again to see HTTP 409 `duplicate_project`.

ZIP import takes a raw ZIP body, rather than a multipart form. Create a ZIP from the contents of either other project, then upload it:

```sh
(cd experiments/drafts/tiny-graph && zip -r /tmp/streetflow-tiny-project.zip .)
curl -i http://localhost:8080/api/v1/projects/import/zip \
  -H 'Content-Type: application/zip' \
  --data-binary @/tmp/streetflow-tiny-project.zip
```

Archives may contain project files at the root or inside a project folder. They must contain exactly one `experiment.json`. Folder import reads a server-side path; ZIP upload works when project files are on the client machine.

## Endpoints and validation

| Request | Result |
| --- | --- |
| `POST /api/v1/projects/import/folder` | JSON `{ "path": "absolute/server/path" }`; validated import, HTTP 201 |
| `POST /api/v1/projects/import/zip` | Raw ZIP body; validated import, HTTP 201 |
| `GET /api/v1/projects` | Project manifests, counts, and revisions, sorted by ID |
| `GET /api/v1/projects/{id}` | One project's manifest, counts, and revision |
| `GET /api/v1/projects/{id}/map?offset=0&limit=1000` | Geometry; offset/limit independently apply to lane, junction, and ordinary-edge arrays; totals allow pagination |
| `GET /api/v1/projects/{id}/demand` | Full validated demand specification |

Validation rejects unknown/duplicate JSON fields, missing required fields/files, unsafe paths, symlinks, archive path collisions, malformed XML, XML external entities, invalid lane permissions/connections, authored traffic, and nonempty detector data. Every ordinary edge usable by a positive-weight vehicle class must have exactly one profile. Intervals must cover `[0, 86400)` without gaps or overlaps, and all weights/settings must be finite and valid. Additional XML accepts signal programs only. Errors name the relevant file and, where applicable, edge.

Errors return `{ "code": "invalid_project", "message": "..." }` with HTTP 400; unknown projects return 404; duplicate IDs return 409; import limits return 413 `project_too_large`. Invalid pagination returns 400 `invalid_pagination`. Existing IDs are never overwritten.

## Persistence and limits

Imports copy inputs into `Simulator__DataRoot/projects/<storage-id>/package/`, with catalog metadata stored outside the package. Validation finishes before the directory is committed. Failed imports remove temporary files. Changing or deleting the source folder does not affect the stored copy. On restart, stored inputs are validated and checked against their recorded revision; changed inputs fail startup with an explicit error. The revision covers referenced simulation inputs, excluding unrelated notes.

`Simulator__MaximumProjectBytes` defaults to 268435456 (256 MiB), applied to the ZIP upload and total copied/expanded file bytes. `Simulator__MaximumProjectFiles` defaults to 4096; directory/archive entry counts are bounded too. The map accepts limits from 1 through 10000. Projects and their full demand specifications remain in memory after validation.

The automated project-loading checks exercise tiny graph (8 usable edges), central Iași (713), and Podu Roș (106), folder/ZIP imports, map inspection without an engine, restart persistence, invalid profiles/files, unsafe archives, duplicate imports, and resource limits.

Task 3 verification: 47 project-loading cases passed; the complete SUMO regression suite passed 69 tests, with one PostgreSQL integration test skipped because no test database was configured. A live HTTP smoke check imported all three projects and confirmed zero runs after import.

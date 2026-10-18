# Projects folder

Balthasar lists every project in this folder. Each entry is either:

- a folder with `experiment.json` at its root, or
- a `.zip` file containing one project (flat, or inside a single folder).

Add, edit or remove projects here, then click **Refresh** in Balthasar's Loader. Folders are read in place; ZIP files are extracted into the simulator's data volume once per file version. Entries whose names start with `.` and other files (such as this guide) are ignored. Invalid entries appear in the list as unavailable, with the reason.

The bundled examples are versioned under `projects/drafts/`. The simulator scans only the immediate children of its projects root. Point Compose at that folder to use the examples directly:

```sh
STREETFLOW_PROJECTS_DIR=./projects/drafts docker compose up --build -d --wait
```

For your own projects, place each folder or ZIP in a separate directory and set `STREETFLOW_PROJECTS_DIR=/absolute/path` in `.env`. Docker mounts the selected folder read-only at `/projects`. Run `docker compose up -d simulator` after changing the path; this recreates the simulator container and stops any loaded simulation. The Docker Desktop camera demo copies the bundled examples to its project volume, so it works without host folder sharing.

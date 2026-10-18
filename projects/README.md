# Projects folder

Balthasar lists every project in this folder. Each entry is either:

- a folder with `experiment.json` at its root, or
- a `.zip` file containing one project (flat, or inside a single folder).

Add, edit or remove projects here, then click **Refresh** in Balthasar's Loader. Folders are read in place; ZIP files are extracted into the simulator's data volume once per file version. Entries whose names start with `.` and other files (such as this guide) are ignored. Invalid entries appear in the list as unavailable, with the reason.

Project folders and ZIP files here can be tracked in git. To start with the bundled examples:

```sh
cp -r experiments/drafts/* projects/
```

Docker mounts this folder read-only at `/projects`. To use another folder on this machine, set `STREETFLOW_PROJECTS_DIR=/absolute/path` in the repository's `.env` file and run `docker compose up -d simulator`. This recreates only the simulator container (stopping any loaded simulation); no rebuild is needed.

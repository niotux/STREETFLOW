# Local Docker preview

From the repository root, using Docker Desktop:

```sh
docker --context desktop-linux compose -f compose.yaml -f compose.preview.yaml --profile cameras up --build -d --wait
```

Open http://localhost:8090/ for the complete MAGI frontend. Balthasar is at /balthasar/, Casper at /casper/, and Melchior at /melchior/. The simulator API and standalone inspector are on http://localhost:8080/.

The camera-demo container sends explicitly synthetic vehicle counts to Casper. It does not access a physical camera. Melchior defaults to the simulator; to inspect camera queues, set its Connection address to http://casper, port 8080. Switch back to http://simulator, port 8080, for simulation light control.

The local, git-ignored .env contains CASPER_DEMO_CAMERA_TOKEN and STREETFLOW_PROJECTS_DIR. This machine uses ./projects/drafts because the project folders were moved there. Both Casper and camera-demo use the same token. On a fresh machine, create .env with a randomly generated token; never commit it.

The preview override adds restart policies and the camera demo. Services appear together under streetflow-simulation in Docker Desktop. Named volumes retain simulator recordings and saved Melchior settings. Projects are mounted read-only from the host.

Stop the preview without deleting its data:

```sh
docker --context desktop-linux compose -f compose.yaml -f compose.preview.yaml --profile cameras stop
```

Start it again:

```sh
docker --context desktop-linux compose -f compose.yaml -f compose.preview.yaml --profile cameras up -d --wait
```

The host ports 8080, 8081, 8082, and 8090 must be available. Real Pi workers can use the same Casper API, but require a configured private-network endpoint, actual camera calibration, and per-camera tokens. The current ports bind to localhost.

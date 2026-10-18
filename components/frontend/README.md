# MAGI frontend

The separate .NET 10 frontend serves plain HTML, CSS, and JavaScript. It opens on a component selector:

- **Melchior** (`/melchior/`): manual traffic light controls and saved taco connection settings.
- **Balthasar** (`/balthasar/`): the existing simulator studio, including Runner, Loader, Analyzer, SUMO, and the legacy inspector.
- **Casper** (`/casper/`): a map of traffic-light and camera devices. Select a device to see its counts and camera picture; open the counting-area editor from that camera.

## Run

```sh
docker compose up --build -d --wait
```

Open **http://localhost:8090**. Set `FRONTEND_PORT`, `LOGISTICS_PORT`, and `SIMULATOR_PORT` to change host ports. Container connections continue to use port 8080.

For native execution, start Balthasar and Melchior in separate terminals, then:

```sh
dotnet run --project components/frontend/Streetflow.Frontend
```

Defaults are frontend port 8090, simulator port 8080, and logistics port 8081. Configure `Frontend__SimulatorBaseUrl` and `Frontend__LogisticsBaseUrl` for other backend addresses; use `--urls` or `URLS` to change the listener.

Casper defaults to port 8082; configure `Frontend__CasperBaseUrl`. Casper starts in the ordinary Compose stack; the optional real-video worker is described in the [camera setup guide](../detector/README.md). The frontend proxies `/casper-api/*` to Casper's `/api/v1/taco/*` and `/casper-camera-api/*` to Casper's camera endpoints. Melchior also displays traffic for its selected stoplight when connected to a TACO traffic provider.

For a ready-to-run camera example, start Docker Desktop and run `./scripts/casper-demo.sh start` from the repository root, then open `http://localhost:8090/casper/`. The sample video is included, and the launcher prepares the model and camera settings. Use `./scripts/casper-demo.sh stop` to stop it. The demo uses port 8090 by default, so stop any other frontend using that port first.

## Source and requests

Balthasar assets are included directly from `components/simulator/Streetflow.Simulator/wwwroot` during build and publish, under `wwwroot/balthasar`. There is one source for the simulator UI; the simulator also retains its standalone inspector on port 8080. The frontend serves build output, so rebuild after browser asset edits.

The frontend streams `/api/v1/*` to Balthasar and `/melchior-api/*` to Melchior's `/api/v1/*`. Browser requests share the frontend origin and need no CORS setup. Melchior connects to taco independently using its saved Connection settings.

The frontend does not start other processes itself; Compose starts and checks all services. New assets are under `Streetflow.Frontend/wwwroot`. Styles and traffic-light map helpers reuse Balthasar's assets.

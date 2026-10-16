# MAGI frontend

The separate .NET 10 frontend serves plain HTML, CSS, and JavaScript. It opens on a component selector:

- **Melchior** (`/melchior/`): manual traffic light controls and saved tlcapi connection settings.
- **Balthasar** (`/balthasar/`): the existing simulator studio, including Runner, Loader, Analyzer, SUMO, and the legacy inspector.
- **Casper**: disabled until the detector is implemented.

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

## Source and requests

Balthasar assets are included directly from `components/simulator/Streetflow.Simulator/wwwroot` during build and publish, under `wwwroot/balthasar`. There is one source for the simulator UI; the simulator also retains its standalone inspector on port 8080. The frontend serves build output, so rebuild after browser asset edits.

The frontend streams `/api/v1/*` to Balthasar and `/melchior-api/*` to Melchior's `/api/v1/*`. Browser requests share the frontend origin and need no CORS setup. Melchior connects to tlcapi independently using its saved Connection settings.

The frontend does not start other processes itself; Compose starts and checks all services. New assets are under `Streetflow.Frontend/wwwroot`. Styles and traffic-light map helpers reuse Balthasar's assets.

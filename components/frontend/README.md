# MAGI frontend

The separate .NET 10 frontend serves plain HTML, CSS, and JavaScript. It opens on a component selector:

- **Melchior** (`/melchior/`): manual traffic light controls and saved taco connection settings.
- **Balthasar** (`/balthasar/`): the existing simulator studio, including Runner, Loader, Analyzer, SUMO, and the legacy inspector.
- **Casper** (`/casper/`): camera counts and queue estimates by street/lane/stoplight, freshness, and a browser calibration editor at `/casper/calibration.html`.

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

Casper defaults to port 8082; configure `Frontend__CasperBaseUrl`. Use the optional Compose `cameras` profile and [camera setup guide](../detector/README.md). The frontend proxies `/casper-api/*` to Casper's `/api/v1/taco/*`. Melchior also displays traffic for its selected stoplight when connected to a TACO traffic provider.

## Source and requests

Balthasar assets are included directly from `components/simulator/Streetflow.Simulator/wwwroot` during build and publish, under `wwwroot/balthasar`. There is one source for the simulator UI; the simulator also retains its standalone inspector on port 8080. The frontend serves build output, so rebuild after browser asset edits.

The frontend streams `/api/v1/*` to Balthasar and `/melchior-api/*` to Melchior's `/api/v1/*`. Browser requests share the frontend origin and need no CORS setup. Melchior connects to taco independently using its saved Connection settings.

The frontend does not start other processes itself; Compose starts and checks all services. New assets are under `Streetflow.Frontend/wwwroot`. Styles and traffic-light map helpers reuse Balthasar's assets.

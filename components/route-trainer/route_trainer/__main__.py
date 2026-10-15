import argparse
import json
import tempfile
from pathlib import Path

from .dataset import validate
from .projects import export_project, load_project


def main():
    parser = argparse.ArgumentParser(description="Streetflow standalone route trainer")
    commands = parser.add_subparsers(dest="command", required=True)
    serve = commands.add_parser("serve")
    serve.add_argument("--host", default="127.0.0.1")
    serve.add_argument("--port", type=int, default=8092)
    for name in ("validate", "train"):
        command = commands.add_parser(name)
        command.add_argument("--project", required=True)
        command.add_argument("--dataset", required=True)
        if name == "train":
            command.add_argument("--output", required=True)
            command.add_argument("--id", required=True)
            command.add_argument("--name", required=True)
            command.add_argument("--epochs", type=int, default=200)
            command.add_argument("--seed", type=int, default=42)
    args = parser.parse_args()
    if args.command == "serve":
        import uvicorn
        uvicorn.run("route_trainer.server:app", host=args.host, port=args.port)
        return
    source, manifest, network = load_project(args.project)
    dataset = json.loads(Path(args.dataset).read_text())
    rows, periods = validate(dataset, network)
    if args.command == "validate":
        print(json.dumps({"routes": len(rows), "periods": periods}, indent=2))
        return
    from .training import train
    destination = Path(args.output).resolve()
    if destination.exists() or destination.is_relative_to(source.resolve()):
        raise ValueError("Output must be a new folder outside the source project")
    with tempfile.TemporaryDirectory() as temporary:
        trained = Path(temporary) / "trained"
        model, report = train(network, dataset, trained, args.epochs, args.seed, lambda value: print(json.dumps(value), flush=True))
        export_project(source, trained, destination, args.id, args.name)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()

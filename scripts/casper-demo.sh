#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"

docker_context=desktop-linux
project=streetflow-casper-easy
asset_dir="$repo_dir/.data/casper-demo"
model_file="$asset_dir/yolox_nano.onnx"
token_file="$asset_dir/easy-token"
compose=(docker --context "$docker_context" compose -p "$project" -f compose.yaml -f compose.preview.yaml -f compose.easy-demo.yaml --profile camera-demo)

export FRONTEND_PORT="${FRONTEND_PORT:-8090}"
export LOGISTICS_PORT="${LOGISTICS_PORT:-18081}"
export CASPER_PORT="${CASPER_PORT:-18082}"
export SIMULATOR_PORT="${SIMULATOR_PORT:-18080}"
export CAMERA_DEMO_VIDEO="$repo_dir/assets/casper-demo/cars-night-960.mp4"
export CAMERA_DEMO_MODEL="$model_file"

if [[ "${1:-start}" == stop ]]; then
  export CASPER_DEMO_CAMERA_TOKEN=stopping
  "${compose[@]}" down
  echo "Sample demo stopped. The saved camera area remains in its Docker volume."
  exit 0
fi

if [[ "${1:-start}" != start ]]; then
  echo "Usage: $0 [start|stop]" >&2
  exit 2
fi

for tool in docker curl openssl tar; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "Install $tool before starting the sample demo." >&2
    exit 1
  fi
done

if ! docker --context "$docker_context" info >/dev/null 2>&1; then
  echo "Start Docker Desktop first. This demo uses its desktop-linux context." >&2
  exit 1
fi

mkdir -p "$asset_dir"
if [[ ! -s "$token_file" ]]; then
  (umask 077; openssl rand -hex 24 > "$token_file")
fi
export CASPER_DEMO_CAMERA_TOKEN="$(cat "$token_file")"

if [[ ! -s "$model_file" ]]; then
  echo "Downloading the detector model…"
  curl --location --fail --retry 3 --output "$model_file.tmp" \
    'https://github.com/Megvii-BaseDetection/YOLOX/releases/download/0.1.1rc0/yolox_nano.onnx'
  mv "$model_file.tmp" "$model_file"
fi

if [[ ! -s assets/casper-demo/cars-night-960.mp4 ]]; then
  echo "The sample video is missing from assets/casper-demo/." >&2
  exit 1
fi

copy_to_volume() {
  local source_file="$1"
  local volume_name="$2"
  local target_name="$3"
  docker --context "$docker_context" volume create "${project}_${volume_name}" >/dev/null
  docker --context "$docker_context" run --rm -i \
    -v "${project}_${volume_name}:/target" alpine:3.21 \
    sh -c "cat > /target/$target_name && chmod 644 /target/$target_name" < "$source_file"
}

echo "Preparing the sample video and camera…"
docker --context "$docker_context" volume create "${project}_demo-projects" >/dev/null
tar -C projects/drafts -cf - . | docker --context "$docker_context" run --rm -i \
  -v "${project}_demo-projects:/target" alpine:3.21 tar -xf - -C /target
copy_to_volume components/detector/examples/configuration.json demo-config configuration.json
copy_to_volume components/detector/examples/camera.json demo-config camera.json
copy_to_volume assets/casper-demo/cars-night-960.mp4 demo-video source
copy_to_volume "$model_file" demo-model model.onnx

"${compose[@]}" up --build -d --wait
echo
echo "Open http://localhost:${FRONTEND_PORT}/casper/"
echo "To stop it: ./scripts/casper-demo.sh stop"

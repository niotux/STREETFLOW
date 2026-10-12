#!/bin/sh
set -eu
# Native binaries only; SUMO's Python tool collection is not installed.
case "$(dpkg --print-architecture)" in
  amd64) architecture=amd64; checksum=53f808c42bd001337de22914a6802117d9dccacda20c1be94ddda2ef919011af ;;
  arm64) architecture=arm64; checksum=fd9e61bafec3d3b12d7db5fd519e870942d83f2948a70efb39f8f52c69456fb7 ;;
  *) echo 'Supported container architectures: amd64 and arm64' >&2; exit 1 ;;
esac
apt-get update -qq
apt-get install -y --no-install-recommends ca-certificates curl
curl --fail --location --retry 3 \
  "https://ppa.launchpadcontent.net/sumo/stable/ubuntu/pool/main/s/sumo/sumo_1.28.0-1_${architecture}.deb" \
  --output /tmp/sumo.deb
printf '%s  /tmp/sumo.deb\n' "$checksum" | sha256sum --check -
apt-get install -y --no-install-recommends /tmp/sumo.deb
rm -rf /tmp/sumo.deb /var/lib/apt/lists/*

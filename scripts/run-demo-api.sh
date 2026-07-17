#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

echo "==> Building and starting the NotifyRail demo stack"
docker compose \
  --file compose.yaml \
  --file compose.demo.yaml \
  up --detach --build --wait api

echo "==> NotifyRail demo API is ready at http://localhost:5012"
echo "    Run ./scripts/demo-flow.sh to execute the demo."

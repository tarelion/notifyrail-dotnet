#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

cleanup() {
  docker compose --profile test rm --stop --force test test-postgres >/dev/null
}

trap cleanup EXIT

docker compose --profile test up --build \
  --abort-on-container-exit \
  --exit-code-from test \
  test

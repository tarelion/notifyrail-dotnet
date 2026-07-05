#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

PORT="${PORT:-5012}"
BASE_URL="http://localhost:${PORT}"

echo "==> Starting PostgreSQL"
docker compose up -d --wait postgres

echo "==> Applying EF Core migrations"
dotnet tool restore >/dev/null
dotnet ef database update --project src/NotifyRail.Api >/dev/null

echo "==> Starting NotifyRail demo API at ${BASE_URL}"
echo "    Mock provider: accepted, retryable->accepted, permanent failure"
echo "    Retry delay: 3 seconds"

env \
  ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="$BASE_URL" \
  Logging__LogLevel__Default=Warning \
  Logging__LogLevel__Microsoft__EntityFrameworkCore=Warning \
  'Logging__LogLevel__Microsoft.AspNetCore=Warning' \
  DeliveryWorker__BatchSize=3 \
  DeliveryWorker__PollInterval=00:00:00.250 \
  DeliveryQueue__BaseRetryDelay=00:00:03 \
  MockProvider__Rules__0__Recipient="+905552222222" \
  MockProvider__Rules__0__Outcomes__0=retryable_failure \
  MockProvider__Rules__0__Outcomes__1=accepted \
  MockProvider__Rules__1__Recipient="+905553333333" \
  MockProvider__Rules__1__Outcomes__0=permanent_failure \
  dotnet run --project src/NotifyRail.Api --no-launch-profile

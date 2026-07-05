# NotifyRail .NET

NotifyRail is a learning-focused C#/.NET backend that simulates reliable
notification delivery. This repository is now the active implementation; the
earlier Go project is the source we are porting from and keeping behaviorally
aligned with.

## Current Implementation

The repository currently provides:

- process liveness and PostgreSQL readiness endpoints
- atomic message creation with one delivery per recipient
- globally unique idempotency keys with replay and conflict handling
- PostgreSQL delivery claiming with scheduling, expiry, priority ordering, and
  `FOR UPDATE SKIP LOCKED`
- five-minute lease recovery for deliveries abandoned in `processing`
- a hosted background worker that sends claimed deliveries through an
  in-process mock provider
- atomic provider-result recording for `accepted`, `retryable_failure`, and
  `permanent_failure` outcomes, including `sent`, `retry_scheduled`, and
  `failed` transitions
- recipient-level delivery reads with ordered provider attempt history
- aggregate message reports with counts for every delivery status

The mock provider currently accepts every valid send. Provider callbacks, OTP
verification, and message summary reads remain planned MVP work. The
[PRD](docs/prd-notifyrail.md) describes the target MVP, not current
implementation status.

## Requirements

- .NET SDK 10.0.x
- Docker, for local PostgreSQL

On Arch Linux, install the SDK and ASP.NET Core runtime from the official repos:

```sh
sudo pacman -S --needed dotnet-sdk aspnet-runtime aspnet-targeting-pack
```

## Run Locally

Start PostgreSQL:

```sh
docker compose up -d --wait postgres
```

Apply EF Core migrations:

```sh
dotnet tool restore
dotnet ef database update --project src/NotifyRail.Api
```

Start the API:

```sh
dotnet run --project src/NotifyRail.Api
```

In another terminal, check the service:

```sh
curl http://localhost:5012/healthz
curl http://localhost:5012/readyz
```

Create a message and its per-recipient deliveries:

```sh
curl --request POST http://localhost:5012/messages \
  --header 'Content-Type: application/json' \
  --data '{
    "type": "transactional",
    "channel": "sms",
    "sender_title": "NotifyRail",
    "body": "Your order is ready.",
    "recipients": ["+905551111111", "+905552222222"],
    "idempotency_key": "order-42-ready"
  }'
```

A successful request returns `202 Accepted` with the message ID, delivery count,
and creation time. The hosted delivery worker will claim due deliveries in the
background and send them through the mock provider. See the
[HTTP API reference](docs/reference/http-api.md) for the complete current
contract.

Stop PostgreSQL:

```sh
docker compose down
```

To also delete local PostgreSQL data, use `docker compose down -v`.

## Development

The API reads PostgreSQL from `ConnectionStrings:Postgres`. The development
configuration expects:

```text
Host=localhost;Port=5432;Database=notifyrail;Username=notifyrail;Password=notifyrail
```

Useful commands:

```sh
docker compose up -d --wait postgres
dotnet restore
dotnet build NotifyRail.slnx
dotnet test NotifyRail.slnx
```

When changing EF models or persistence mappings, create and apply a migration:

```sh
dotnet ef migrations add <MigrationName> --project src/NotifyRail.Api
dotnet ef database update --project src/NotifyRail.Api
```

## Tests

Run the suite:

```sh
docker compose up -d --wait postgres
dotnet test NotifyRail.slnx
```

The current integration tests use PostgreSQL. Keep the database container
running before executing the full suite.

## Implemented Endpoints

- `GET /healthz`: process liveness
- `GET /readyz`: PostgreSQL connectivity
- `POST /messages`: idempotent message and delivery creation
- `GET /messages/{message_id}/deliveries`: recipient delivery states and
  attempt history
- `GET /messages/{message_id}/report`: aggregate delivery status counts

## Documentation

Start with the [documentation index](docs/README.md). Canonical contracts live
under `docs/reference/`; product direction lives in the PRD. Existing .NET
architecture decisions live under `docs/adr/`.

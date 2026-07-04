# Repository Guidelines

## Project Structure

- `src/NotifyRail.Api/Program.cs` is the executable entry point and runtime
  wiring.
- `src/NotifyRail.Api/Features/Health` owns liveness and readiness endpoints.
- `src/NotifyRail.Api/Features/Messages` owns message intake and persistence.
- `src/NotifyRail.Api/Features/Deliveries` owns delivery persistence, queueing,
  provider adapters, and worker processing.
- `src/NotifyRail.Api/Infrastructure/Persistence` owns the EF Core DbContext and
  migrations.
- `tests/NotifyRail.Api.Tests` contains xUnit tests. PostgreSQL-backed behavior
  is tested through integration-style public interfaces.
- Start documentation lookup at `docs/README.md`; canonical contracts live in
  `docs/reference/`; architectural decisions live in `docs/adr/`.

## Build, Test, and Development Commands

- `docker compose up -d --wait postgres`: start the local PostgreSQL database.
- `dotnet restore`: restore NuGet packages.
- `dotnet build NotifyRail.slnx`: build the solution.
- `dotnet test NotifyRail.slnx`: run the test suite. PostgreSQL must be running.
- `dotnet run --project src/NotifyRail.Api`: run the API on the host.
- `dotnet tool restore`: restore local tools, including `dotnet-ef`.
- `dotnet ef database update --project src/NotifyRail.Api`: apply EF Core
  migrations.
- `dotnet ef migrations add <Name> --project src/NotifyRail.Api`: add a schema
  migration after model or mapping changes.

## Coding Style

Target the .NET version declared in the project files. Keep nullable reference
types enabled and prefer existing feature-folder patterns over new layers.

Use small, behavior-oriented modules:

- HTTP endpoint classes normalize transport concerns.
- Domain/application modules own behavior and persistence semantics.
- EF Core configuration classes own table, column, index, and constraint
  mapping.
- Queue and worker modules hide PostgreSQL locking, retries, and provider
  result recording behind small public interfaces.

Preserve the separation between `Message`, `Delivery`, and `DeliveryAttempt`.
Do not hand-edit generated build output under `bin/` or `obj/`.

## Testing Guidelines

Name tests by observable behavior, such as
`ClaimDueAsync_ClaimsDueDeliveryAndReturnsProviderRequest`. Prefer testing
through public interfaces and HTTP endpoints instead of private helpers.

Run `dotnet test NotifyRail.slnx` for every behavior change. For persistence,
transaction, idempotency, worker, or concurrency behavior, keep PostgreSQL
running with `docker compose up -d --wait postgres`.

When a hosted background worker would race a focused queue or endpoint test,
use the existing test helper that removes hosted services.

## Commit and Pull Request Guidelines

Keep each commit focused and use a short imperative subject, for example
`Add delivery queue claiming` or `Run delivery worker in background`.

Pull requests should explain behavior and motivation, list validation commands,
and call out migrations or configuration changes. For HTTP changes, include a
representative request and response.

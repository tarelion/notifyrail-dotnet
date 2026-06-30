# NotifyRail .NET

NotifyRail is a learning-focused C#/.NET rewrite of the original Go backend.
The goal is to rebuild reliable notification delivery step by step while using
idiomatic ASP.NET Core, tests, and small vertical slices.

## Current state

- ASP.NET Core API project under `src/NotifyRail.Api`
- xUnit test project under `tests/NotifyRail.Api.Tests`
- `GET /healthz` liveness endpoint
- GitHub Actions workflow for restore, build, and test

## Requirements

- .NET SDK 10.0.x

On Arch Linux, install the SDK and ASP.NET Core runtime from the official repos:

```sh
sudo pacman -S --needed dotnet-sdk aspnet-runtime aspnet-targeting-pack
```

Pacman installs `dotnet` at `/usr/bin/dotnet`, which should already be on your
PATH.

## Run locally

```sh
dotnet run --project src/NotifyRail.Api
```

Then check the service:

```sh
curl http://localhost:5000/healthz
```

## Test

```sh
dotnet test
```

## Roadmap

The first learning slices should stay small:

1. Add `/readyz` with configuration-driven PostgreSQL connectivity.
2. Add message creation request/response models.
3. Persist messages and deliveries transactionally.
4. Add delivery claiming and worker processing.

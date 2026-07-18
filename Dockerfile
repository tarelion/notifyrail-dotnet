FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /source

COPY src/NotifyRail.Api/NotifyRail.Api.csproj src/NotifyRail.Api/
RUN dotnet restore src/NotifyRail.Api/NotifyRail.Api.csproj

COPY src/NotifyRail.Api/ src/NotifyRail.Api/
RUN dotnet publish src/NotifyRail.Api/NotifyRail.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /var/lib/notifyrail/data-protection-keys \
    && chown -R $APP_UID /var/lib/notifyrail

WORKDIR /app
COPY --from=build /app/publish/ ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "NotifyRail.Api.dll"]

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS test
WORKDIR /source

COPY NotifyRail.slnx ./
COPY src/NotifyRail.Api/NotifyRail.Api.csproj src/NotifyRail.Api/
COPY tests/NotifyRail.Api.Tests/NotifyRail.Api.Tests.csproj tests/NotifyRail.Api.Tests/
RUN dotnet restore NotifyRail.slnx

COPY src/NotifyRail.Api/ src/NotifyRail.Api/
COPY tests/NotifyRail.Api.Tests/ tests/NotifyRail.Api.Tests/

ENTRYPOINT ["dotnet", "test", "NotifyRail.slnx", "--no-restore"]

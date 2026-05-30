# Multi-stage build for the centralized Featly server sample (server + dashboard
# + SQLite). The build context is the REPOSITORY ROOT, because this sample
# references projects under src/ — see docker-compose.yml and docs/DEPLOYMENT.md.
#
# Build from the repo root with:
#   docker build -f samples/Centralized.Sample/Dockerfile -t featly-centralized .
# or just `docker compose up --build` from samples/Centralized.Sample/.

ARG DOTNET_VERSION=10.0

# ---- build ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Restore layer first: copying the files that drive restore (the central package
# versions, common build props, SDK pin, and the project graph the sample needs)
# lets Docker cache the package restore until one of them actually changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
COPY samples/Centralized.Sample/ samples/Centralized.Sample/
RUN dotnet restore samples/Centralized.Sample/Centralized.Sample.csproj

# Publish. Intentionally NOT trimmed: the server relies on EF Core, reflection-
# based System.Text.Json, and minimal-API metadata, all of which trimming would
# break — this is the "where safe" carve-out from the issue. UseAppHost=false
# skips the native launcher since we start via `dotnet Featly.Samples.Centralized.dll`.
RUN dotnet publish samples/Centralized.Sample/Centralized.Sample.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- runtime --------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

# SQLite lives on a mounted volume so data survives container restarts. Create
# the directory owned by the image's non-root "app" user; a fresh named volume
# inherits this ownership, so the non-root process can write to it.
RUN mkdir -p /data && chown app:app /data
VOLUME ["/data"]

COPY --from=build /app/publish ./

# Point storage at the persisted volume (overrides appsettings) and keep the
# zero-friction auto-migrate on so the schema is created on first boot.
ENV Featly__Storage__Sqlite__ConnectionString="Data Source=/data/featly-central.db" \
    Featly__Storage__Sqlite__AutoMigrate=true

USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Featly.Samples.Centralized.dll"]

# Multi-stage build for the MongoDB-backed centralized Featly server sample
# (server + dashboard) plus the `featly` CLI, used by docker-compose.yml's
# one-shot "migrate" service. The build context is the REPOSITORY ROOT, because
# this sample references projects under src/ — see docker-compose.yml and
# docs/DEPLOYMENT.md.
#
# Build from the repo root with:
#   docker build -f samples/Centralized.MongoDB.Sample/Dockerfile -t featly-centralized-mongodb .
# or just `docker compose up --build` from samples/Centralized.MongoDB.Sample/.

ARG DOTNET_VERSION=10.0

# ---- build ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Restore layer first: copying the files that drive restore (the central package
# versions, common build props, SDK pin, and the project graph the sample needs)
# lets Docker cache the package restore until one of them actually changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
COPY samples/Centralized.MongoDB.Sample/ samples/Centralized.MongoDB.Sample/
RUN dotnet restore samples/Centralized.MongoDB.Sample/Centralized.MongoDB.Sample.csproj
RUN dotnet restore src/Featly.Cli/Featly.Cli.csproj

# Publish the server. Intentionally NOT trimmed: it relies on EF Core (for the
# other providers compiled into the same Featly.Cli image), reflection-based
# System.Text.Json, and minimal-API metadata, all of which trimming would break.
# UseAppHost=false skips the native launcher since we start via
# `dotnet Featly.Samples.CentralizedMongoDB.dll`.
RUN dotnet publish samples/Centralized.MongoDB.Sample/Centralized.MongoDB.Sample.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# Publish the `featly` CLI too — the compose file's "migrate" service runs
# `featly db migrate --provider mongodb ...` once, before any server replica
# starts, per docs/DEPLOYMENT.md's "Turn AutoMigrate off when you run more than
# one replica" guidance.
RUN dotnet publish src/Featly.Cli/Featly.Cli.csproj \
    -c Release -o /app/cli --no-restore /p:UseAppHost=false

# ---- runtime --------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

COPY --from=build /app/publish ./
COPY --from=build /app/cli ./cli

# No volume needed: state lives in the shared MongoDB replica set, not on this
# container's filesystem.

USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Featly.Samples.CentralizedMongoDB.dll"]

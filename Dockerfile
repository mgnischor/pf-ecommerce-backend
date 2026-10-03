# Production images of the pf-ecommerce API (ai/CONTAINERS.md §2, §13.1). Two targets share one build stage:
#
#   runtime     the API and worker image (the default: it is the last stage, so a bare `docker build .` produces it)
#   migrations  one EF Core bundle per bounded context, run as a job before a new version rolls out
#
#   docker build --target runtime \
#     --build-arg VERSION=1.0.0 --build-arg REVISION=$(git rev-parse HEAD) --build-arg CREATED=$(date -u +%FT%TZ) \
#     -t ecommerce-api:1.0.0 .
#
# Base images are pinned to tag AND digest: the tag keeps the version readable, the digest makes the build
# reproducible. Renovate/Dependabot update both together. Rebuild at least monthly to pick up base patches.

# ---------------------------------------------------------------------------------------------------------
# Stage 1: build. The SDK, package caches, and sources never reach the final image.
# ---------------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build

ENV DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true

WORKDIR /src

# Restore has its own layer, keyed only on the files that define the dependency graph. .editorconfig and
# BannedSymbols.txt are build inputs too (analyzers run as errors), so they belong to this layer.
COPY global.json Directory.Build.props Directory.Packages.props BannedSymbols.txt .editorconfig Portfolio.csproj packages.lock.json ./
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore Portfolio.csproj --locked-mode

COPY src/ src/
COPY configuration/ configuration/
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish Portfolio.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish \
        -p:UseAppHost=false \
        -p:DebugType=none \
        -p:DebugSymbols=false \
        -p:ContinuousIntegrationBuild=true

# ---------------------------------------------------------------------------------------------------------
# Migrations (ai/DATABASE.md §6.1, ai/CONTAINERS.md §6.7). One self-contained EF Core bundle per bounded context,
# built from the same sources as the application so schema and code ship together. It is a separate image and
# runs as a job with the DDL-capable role before a new version rolls out, never inside the API:
#
#   docker build --target migrations -t ecommerce-migrations:1.0.0 .
#   docker run --rm -e PF_DESIGN_TIME_CONNECTION_FILE=/run/secrets/conn_postgres_migrator ... /migrations/migrate-identity
#
# Run the bundles in order (identity, catalog, inventory; they are independent). A bundle takes the connection from
# the secret file named by PF_DESIGN_TIME_CONNECTION_FILE, so it never appears on a command line.
# ---------------------------------------------------------------------------------------------------------
FROM build AS bundles

# The bundle is a native executable: build it for the architecture of the image that will run it (BuildKit sets
# TARGETARCH; the legacy builder leaves it empty, which means amd64).
ARG TARGETARCH

COPY .config/ .config/
COPY scripts/build-migrations-bundles.sh scripts/
RUN --mount=type=cache,target=/root/.nuget/packages \
    case "${TARGETARCH:-amd64}" in \
        amd64) runtime=linux-x64 ;; \
        arm64) runtime=linux-arm64 ;; \
        *) echo "Unsupported architecture: ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && bash scripts/build-migrations-bundles.sh "${runtime}" /app/migrations

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0.12-noble-chiseled@sha256:dc5cd0c7d0a39b825312b4f0986d99b9adf9b6a3af69bc23cd345c27d9ed916a AS migrations

ARG VERSION=0.0.0-dev
ARG REVISION=unknown
ARG CREATED=unknown
ARG SOURCE=https://github.com/mgnischor/pf-ecommerce-backend

LABEL org.opencontainers.image.title="pf-ecommerce-migrations" \
      org.opencontainers.image.description="EF Core migrations bundles of the pf-ecommerce bounded contexts" \
      org.opencontainers.image.source="${SOURCE}" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${REVISION}" \
      org.opencontainers.image.created="${CREATED}"

WORKDIR /migrations
COPY --from=bundles /app/migrations/ .

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true

USER $APP_UID

# No ENTRYPOINT: the orchestrator runs one bundle per job step (identity, catalog, inventory).

# ---------------------------------------------------------------------------------------------------------
# Final stage: runtime. Chiseled Ubuntu: no shell, no package manager, no SUID binaries, non-root user built in.
# It is declared last so that building without --target yields the API image, never the migrations job.
# ---------------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble-chiseled@sha256:48e51f2f6798897be7ac4e775c049ed8fe60d3190f637e1f9c9dc7513efa659c AS runtime

ARG VERSION=0.0.0-dev
ARG REVISION=unknown
ARG CREATED=unknown
ARG SOURCE=https://github.com/mgnischor/pf-ecommerce-backend

LABEL org.opencontainers.image.title="pf-ecommerce-api" \
      org.opencontainers.image.description="E-commerce modular monolith API (C# / .NET 10)" \
      org.opencontainers.image.source="${SOURCE}" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${REVISION}" \
      org.opencontainers.image.created="${CREATED}"

WORKDIR /app

# Application files stay root-owned and read-only to the runtime user.
COPY --from=build /app/publish .

# TLS terminates at the reverse proxy; Kestrel only speaks HTTP on 8080 (no capability needed to bind it).
# DOTNET_EnableDiagnostics_IPC=0 closes the diagnostics socket in production (ai/CONTAINERS.md §13.1).
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_HTTP_PORTS= \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics_IPC=0

# Numeric user (the image's built-in non-root account, 1654) so runAsNonRoot can be verified by the orchestrator.
USER $APP_UID

EXPOSE 8080

# No HEALTHCHECK instruction: Kubernetes probes replace it, and Compose defines a healthcheck that runs
# `dotnet Portfolio.dll --health-check` (the image has no shell, curl, or wget).
# Exec form, so SIGTERM reaches the process and the host drains gracefully.
ENTRYPOINT ["dotnet", "Portfolio.dll"]

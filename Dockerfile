# Production image of the pf-ecommerce API (ai/CONTAINERS.md §2, §13.1).
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
FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29 AS build

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
# Stage 2: runtime. Chiseled Ubuntu: no shell, no package manager, no SUID binaries, non-root user built in.
# ---------------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble-chiseled@sha256:9651fa59abcdf177c30392cb44a820605ca5d618429ab37acbf6e7c644510b02 AS runtime

ARG VERSION=0.0.0-dev
ARG REVISION=unknown
ARG CREATED=unknown
ARG SOURCE=https://github.com/OWNER/pf-ecommerce-backend

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
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics_IPC=0

# Numeric user (the image's built-in non-root account, 1654) so runAsNonRoot can be verified by the orchestrator.
USER $APP_UID

EXPOSE 8080

# No HEALTHCHECK instruction: Kubernetes probes replace it, and Compose defines a healthcheck that runs
# `dotnet Portfolio.dll --health-check` (the image has no shell, curl, or wget).
# Exec form, so SIGTERM reaches the process and the host drains gracefully.
ENTRYPOINT ["dotnet", "Portfolio.dll"]

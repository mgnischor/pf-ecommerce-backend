# Container Standards

> **Scope:** These standards apply to all containerized workloads of the `pf-ecommerce-backend` repository: the ASP.NET Core application image (`Dockerfile`), the Docker Compose topologies (`docker-compose-dev.yml`, `docker-compose-prod.yml`), and the Kubernetes manifests (`kubernetes/`). Backing services are PostgreSQL, Valkey, and RabbitMQ. Every item marked as mandatory is a hard requirement unless a documented exception is approved by the maintainers. Container engineering prioritizes security, stability, and reproducibility. All workloads must also comply with [SECURITY.md](./SECURITY.md) and [IAC.md](./IAC.md).

---

## Table of Contents

1. [General Principles](#1-general-principles)
2. [Docker Image Standards](#2-docker-image-standards)
    - 2.1 [Base Image Selection](#21-base-image-selection)
    - 2.2 [Multi-Stage Builds](#22-multi-stage-builds)
    - 2.3 [Layer Optimization](#23-layer-optimization)
    - 2.4 [Dockerfile Hygiene](#24-dockerfile-hygiene)
    - 2.5 [Image Tagging and Versioning](#25-image-tagging-and-versioning)
    - 2.6 [Image Scanning and Signing](#26-image-scanning-and-signing)
3. [Container Runtime Standards](#3-container-runtime-standards)
    - 3.1 [Security Context](#31-security-context)
    - 3.2 [Resource Management](#32-resource-management)
    - 3.3 [Filesystem and Volume Policy](#33-filesystem-and-volume-policy)
    - 3.4 [Networking](#34-networking)
    - 3.5 [Logging and Observability](#35-logging-and-observability)
4. [Container Registry Standards](#4-container-registry-standards)
5. [Docker Compose Standards](#5-docker-compose-standards)
6. [Kubernetes Workload Standards](#6-kubernetes-workload-standards)
    - 6.1 [Pod Design](#61-pod-design)
    - 6.2 [Deployment Strategy](#62-deployment-strategy)
    - 6.3 [Service and Ingress](#63-service-and-ingress)
    - 6.4 [ConfigMaps and Secrets](#64-configmaps-and-secrets)
    - 6.5 [Autoscaling](#65-autoscaling)
    - 6.6 [Pod Disruption Budgets](#66-pod-disruption-budgets)
    - 6.7 [Jobs and CronJobs](#67-jobs-and-cronjobs)
7. [Kubernetes Cluster Operations](#7-kubernetes-cluster-operations)
    - 7.1 [Namespace Strategy](#71-namespace-strategy)
    - 7.2 [Resource Quotas and Limit Ranges](#72-resource-quotas-and-limit-ranges)
    - 7.3 [Network Policies](#73-network-policies)
    - 7.4 [Storage and Stateful Dependencies](#74-storage-and-stateful-dependencies)
    - 7.5 [Cluster Maintenance and Upgrades](#75-cluster-maintenance-and-upgrades)
8. [Health Checks and Resilience](#8-health-checks-and-resilience)
    - 8.1 [Probe Design](#81-probe-design)
    - 8.2 [Graceful Shutdown](#82-graceful-shutdown)
    - 8.3 [Restart Policies and Back-Off](#83-restart-policies-and-back-off)
9. [Performance and Optimization](#9-performance-and-optimization)
    - 9.1 [Image Size Optimization](#91-image-size-optimization)
    - 9.2 [Container Startup Performance](#92-container-startup-performance)
    - 9.3 [Runtime Performance](#93-runtime-performance)
    - 9.4 [Scheduling and Affinity](#94-scheduling-and-affinity)
10. [CI/CD Integration](#10-cicd-integration)
    - 10.1 [Build Pipeline](#101-build-pipeline)
    - 10.2 [Deployment Pipeline](#102-deployment-pipeline)
    - 10.3 [Rollback Strategy](#103-rollback-strategy)
11. [Monitoring, Alerting, and Debugging](#11-monitoring-alerting-and-debugging)
    - 11.1 [Metrics](#111-metrics)
    - 11.2 [Alerting Rules](#112-alerting-rules)
    - 11.3 [Debugging and Troubleshooting](#113-debugging-and-troubleshooting)
12. [Multi-Environment Considerations](#12-multi-environment-considerations)
13. [.NET Container Guidance](#13-net-container-guidance)
    - 13.1 [Base Images and Build Tooling](#131-base-images-and-build-tooling)
    - 13.2 [Health, Shutdown, and Telemetry Libraries](#132-health-shutdown-and-telemetry-libraries)
14. [Container Definition of Done](#14-container-definition-of-done)

---

<GeneralPrinciples>
## 1. General Principles

| Principle | Mandatory Behavior |
| --- | --- |
| **Immutability** | Containers are immutable artifacts. Never patch a running container. Build a new image and redeploy. |
| **Ephemerality** | Containers can be destroyed and recreated at any time. No local state that cannot be recovered from external storage. |
| **Minimal Attack Surface** | Images contain only what is required to run the application. No shells, compilers, or package managers in production images. |
| **Security by Default** | Non-root, read-only filesystem, dropped capabilities, no privilege escalation. See [SECURITY.md](./SECURITY.md). |
| **Reproducibility** | A given image tag or digest always produces the same runtime behavior. Pin base images, SDKs, and tool versions. |
| **Observability from Day One** | Every container emits structured logs to stdout/stderr, exposes health endpoints, and exports telemetry via OpenTelemetry. |
| **Resource Accountability** | Every container declares explicit CPU and memory requests and limits. |
| **Graceful Lifecycle Management** | Containers handle `SIGTERM` and shut down gracefully within the termination grace period. |
| **Least Privilege** | Runtime permissions, network access, and mounted volumes are restricted to the minimum required. |
| **Infrastructure as Code** | Orchestration manifests are version-controlled, reviewed, and deployed through CI/CD. No manual changes on production hosts or clusters. |

</GeneralPrinciples>

---

<DockerImageStandards>
## 2. Docker Image Standards

### 2.1 Base Image Selection

| Requirement | Mandatory Behavior |
| --- | --- |
| **Minimal base images** | Application image: the Microsoft-published **chiseled** ASP.NET image (see [Section 13.1](#131-base-images-and-build-tooling)). Full OS images (`ubuntu`, `debian`) are forbidden in production. Backing-service images use the official `-alpine`/`-slim` variants. |
| **Pin to digest** | Pin base images to SHA256 digests, not floating tags. Keep the tag for readability: `tag@sha256:...`. |
| **Verified publishers only** | Use Docker Official Images, Verified Publishers, or Microsoft Artifact Registry (`mcr.microsoft.com`) images. Unverified third-party images are forbidden. |
| **Regular rebasing** | Rebuild images at least monthly to pick up base-image security patches, even if application code is unchanged. |

```dockerfile
# Correct — pinned to digest
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled@sha256:abc123...

# Forbidden — floating tag
FROM mcr.microsoft.com/dotnet/aspnet:latest
```

Automate digest updates with Renovate or Dependabot (both understand `tag@sha256:`), so pinning does not produce stale images.

### 2.2 Multi-Stage Builds

Multi-stage builds are **mandatory**.

```dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:def456... AS build
WORKDIR /src
# Restore is cached separately from source changes
COPY global.json Directory.Build.props Directory.Packages.props Portfolio.csproj ./
COPY packages.lock.json* ./
RUN dotnet restore Portfolio.csproj --locked-mode
COPY src/ src/
COPY configuration/ configuration/
COPY properties/ properties/
RUN dotnet publish Portfolio.csproj -c Release -o /app/publish --no-restore

# Stage 2: Runtime — chiseled image: no shell, no package manager, no SUID binaries
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled@sha256:abc123... AS runtime
WORKDIR /app
# Application files stay root-owned and read-only to the runtime user
COPY --from=build /app/publish .

# The .NET images define a non-root user; $APP_UID resolves to its numeric UID
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# No HEALTHCHECK: Kubernetes probes (Section 8.1) replace it, and the image has no shell or curl.
ENTRYPOINT ["dotnet", "Portfolio.dll"]
```

Adjust `COPY` paths to the actual layout of `Portfolio.csproj`. Because a shell-less final stage cannot execute `RUN`, all file preparation (permissions, ownership) happens in the build stage or through `COPY --chown`/`--chmod`.

| Rule | Mandatory Behavior |
| --- | --- |
| **Build tools excluded** | The SDK, test frameworks, and package caches never appear in the final stage. |
| **Minimal COPY** | Copy only the published output. Never copy source code into the runtime stage. |
| **Separate test stage (optional)** | A dedicated test stage may run unit tests during the build; the build fails on test failure. |

### 2.3 Layer Optimization

| Rule | Mandatory Behavior |
| --- | --- |
| **Combine RUN commands** | Merge related `RUN` instructions; clean caches in the same instruction. |
| **Order by change frequency** | Put rarely changing instructions (restore) before frequently changing ones (source). |
| **Leverage build cache** | `dotnet restore` has its own layer keyed on project/lock files; use BuildKit cache mounts for the NuGet cache (`RUN --mount=type=cache,target=/root/.nuget/packages`). |
| **Remove unnecessary files per layer** | Delete temp files and package caches in the same `RUN` step that creates them. |

### 2.4 Dockerfile Hygiene

| Requirement | Mandatory Behavior |
| --- | --- |
| **`.dockerignore`** | Maintain a `.dockerignore` that excludes `.git`, `.env`, `bin/`, `obj/`, `ai/`, `docs/`, test results, IDE files, and secrets. |
| **No secrets in images** | Never use `ENV`, `ARG`, `COPY`, or `ADD` to embed credentials, keys, or certificates. Inject at runtime from a secrets manager. NuGet feed credentials use BuildKit secret mounts (`--mount=type=secret`). |
| **No SUID/SGID binaries** | The chiseled base satisfies this by construction. Verify with the image scanner. |
| **Health checking** | Kubernetes ignores `HEALTHCHECK`; it relies on probes ([Section 8.1](#81-probe-design)). Images run by Docker Compose define a Compose-level `healthcheck` in exec form using a binary shipped in the image (e.g., the application's own `--health-check` mode) — never add a shell, `curl`, or `wget` to the chiseled image. |
| **ENTRYPOINT in exec form** | `ENTRYPOINT ["dotnet", "Portfolio.dll"]` (JSON array). Shell form prevents `SIGTERM` forwarding. |
| **LABEL metadata** | Include `org.opencontainers.image.source`, `.version`, `.revision`, `.created`. |
| **Non-root user** | Set `USER $APP_UID` (numeric) before `ENTRYPOINT` so `runAsNonRoot` can verify it. Never run the final process as root. |
| **No ADD for remote URLs** | Use `COPY` for local files. Remote downloads use `RUN` with checksum verification, in the build stage only. |

### 2.5 Image Tagging and Versioning

| Requirement | Mandatory Behavior |
| --- | --- |
| **Semantic version tag** | `ecommerce-api:1.4.2`. |
| **Git SHA tag** | Additionally `ecommerce-api:a1b2c3d` for exact traceability. |
| **No `latest` in production** | `latest` is never referenced by production manifests or pipelines. |
| **Immutable tags** | A pushed tag is never overwritten. Enable registry tag immutability. |
| **Build metadata labels** | Embed build timestamp, Git SHA, branch, and pipeline run ID as labels. |
| **Multi-architecture builds** | When nodes span `amd64` and `arm64`, publish a multi-arch manifest with `docker buildx`. |

### 2.6 Image Scanning and Signing

| Requirement | Mandatory Behavior |
| --- | --- |
| **Scan on every build** | Trivy, Grype, Docker Scout, or Snyk Container in CI. Block on any `HIGH`/`CRITICAL` CVE. |
| **Scan running images** | In Kubernetes, run a continuous scanner (Trivy Operator) to catch CVEs disclosed after deployment. |
| **Sign production images** | Sigstore/Cosign (keyless OIDC preferred) or Notation. Only signed images are deployed to staging and production. |
| **Verify at admission** | In Kubernetes, enforce signature verification with Kyverno, OPA/Gatekeeper, or the Sigstore Policy Controller. |
| **SBOM and provenance** | Generate an SBOM for every production image (`syft`, `docker buildx --sbom=true`, or Trivy) and attach SBOM and SLSA provenance as OCI attestations. |

</DockerImageStandards>

---

<ContainerRuntimeStandards>
## 3. Container Runtime Standards

### 3.1 Security Context

All containers run with the most restrictive security context possible.

| Requirement | Mandatory Behavior |
| --- | --- |
| **Non-root execution** | `USER` set in the Dockerfile; `runAsNonRoot: true` in Kubernetes. |
| **No privilege escalation** | `allowPrivilegeEscalation: false` (Kubernetes) / `security_opt: ["no-new-privileges:true"]` (Compose). |
| **Drop all capabilities** | `capabilities.drop: ["ALL"]` / `cap_drop: ["ALL"]`. Binding port 8080 needs no capability. |
| **Seccomp profile** | `RuntimeDefault` at minimum. |
| **AppArmor / SELinux** | Enforce profiles where the runtime supports them. |
| **Read-only root filesystem** | `readOnlyRootFilesystem: true` / `read_only: true`. Mount `emptyDir`/`tmpfs` at `/tmp` (the .NET runtime needs a writable temp directory). |
| **Pod Security Admission** | Application namespaces are labeled for the `restricted` Pod Security Standard ([SECURITY.md §10.2](./SECURITY.md#102-kubernetes)); non-compliant Pods are rejected at admission. |

### 3.2 Resource Management

| Requirement | Mandatory Behavior |
| --- | --- |
| **CPU and memory requests and limits** | Declared on every container. |
| **Requests ≤ limits** | Requests reflect typical consumption; limits the tolerable burst. |
| **PID limits** | Set `pids_limit` (Compose) or kubelet PID limiting. |
| **Ephemeral storage limits** | Set `resources.limits.ephemeral-storage` in Kubernetes. |
| **No unbounded containers** | Enforce with `LimitRange` and admission policies in Kubernetes; `deploy.resources.limits` in Compose. |
| **QoS class** | Use `Guaranteed` (requests = limits) for latency-critical workloads; `Burstable` is acceptable for most services; `BestEffort` is forbidden in production. |

```yaml
resources:
    requests:
        cpu: "250m"
        memory: "256Mi"
        ephemeral-storage: "100Mi"
    limits:
        cpu: "1000m"
        memory: "512Mi"
        ephemeral-storage: "500Mi"
```

### 3.3 Filesystem and Volume Policy

| Requirement | Mandatory Behavior |
| --- | --- |
| **No `hostPath` in production** | Use `PersistentVolumeClaim`, `emptyDir`, or CSI drivers (Kubernetes); named volumes (Compose). |
| **Temporary directories** | Use `emptyDir` (`medium: Memory`) or `tmpfs` with a `sizeLimit`. |
| **Persistent data externalized** | The application container is stateless. State lives in PostgreSQL, Valkey, and RabbitMQ only. |
| **No Docker socket mounting** | Mounting `/var/run/docker.sock` is forbidden. |

### 3.4 Networking

| Requirement | Mandatory Behavior |
| --- | --- |
| **Expose only required ports** | Only the application port (8080). Do not expose debug or profiling endpoints. |
| **Backing services are not published** | PostgreSQL (5432), Valkey (6379), and RabbitMQ (5672, 15672) are reachable only on the internal network in production — never published on the host. In development, bind published ports to `127.0.0.1` only. |
| **No `hostPort`** | Use `Service` and `Ingress`. |
| **TLS for all external traffic** | Terminate TLS at the ingress/reverse proxy. Use TLS for connections to PostgreSQL, Valkey, and RabbitMQ in production. The application honors `X-Forwarded-*` headers only from trusted proxies (`ForwardedHeadersOptions.KnownProxies/KnownNetworks`). |
| **DNS policy** | `ClusterFirst` by default. |

### 3.5 Logging and Observability

| Requirement | Mandatory Behavior |
| --- | --- |
| **Log to stdout/stderr** | Write console logs to stdout/stderr for diagnostics (`kubectl logs`, `docker logs`). The canonical shipping path is OTLP to the Collector ([OBSERVABILITY.md Section 4.3](./OBSERVABILITY.md#43-log-shipping-path)); no log agent tails the application's stdout. Never write log files inside the container; the file channel in [SECURITY.md §11.5.2](./SECURITY.md#1152-mandatory-output-channels) is disabled for containers. |
| **Structured logging** | JSON with ISO 8601 UTC timestamp, severity, `trace_id`/`span_id`, and service name ([OBSERVABILITY.md](./OBSERVABILITY.md)). |
| **No sensitive data in logs** | See [SECURITY.md §11.2](./SECURITY.md#112-what-never-to-log). |
| **Telemetry export** | The application exports logs, metrics, and traces via OTLP to an OpenTelemetry Collector (`OTEL_EXPORTER_OTLP_ENDPOINT`). A Prometheus scrape endpoint is optional and only for infrastructure that requires it. |
| **Trace propagation** | Propagate W3C `traceparent`/`tracestate` across HTTP calls and RabbitMQ messages. |

</ContainerRuntimeStandards>

---

<ContainerRegistryStandards>
## 4. Container Registry Standards

| Requirement | Mandatory Behavior |
| --- | --- |
| **Private registry for production** | Production images live in a private, authenticated registry (GHCR, ACR, ECR, Artifact Registry, Harbor). |
| **Mirror public images** | Public base images used in production are mirrored, scanned, and signed before use. Never pull directly from public registries at runtime. |
| **Tag immutability** | Enabled. |
| **Retention policy** | Automated retention removes untagged and old images; retain the last N tagged versions. |
| **Access control** | Least-privilege RBAC: CI pushes; runtime hosts/nodes pull. |
| **Vulnerability scanning** | The registry's built-in scanning is enabled; images with unresolved `CRITICAL` CVEs are not pulled. |
| **Audit logging** | Enabled for pushes, pulls, deletions, and permission changes. |

</ContainerRegistryStandards>

---

<DockerComposeStandards>
## 5. Docker Compose Standards

Compose is used in two roles, both version-controlled in the repository root:

| File | Role | Policy |
| --- | --- | --- |
| `docker-compose-dev.yml` | Local development and integration testing: the application plus PostgreSQL, Valkey, RabbitMQ, and the OpenTelemetry Collector (with an optional local telemetry UI). | Convenience allowed (published ports on `127.0.0.1`, development credentials from an untracked `.env`), but the security context, health checks, and resource limits still apply. |
| `docker-compose-prod.yml` | Single-host production deployment. Kubernetes (`kubernetes/`) is the path for multi-node, autoscaled deployments. | Every rule in this section and [Section 3](#3-container-runtime-standards) applies without relaxation. |

| Requirement | Mandatory Behavior |
| --- | --- |
| **Compose Specification** | Omit the obsolete top-level `version:` field. Pin the Compose CLI version in CI. |
| **Service naming** | Lowercase, hyphen-separated: `ecommerce-api`, `postgres`, `valkey`, `rabbitmq`, `otel-collector`. |
| **Secrets** | Use Compose `secrets:` (file-based, untracked files) or an external secrets manager. Never commit `.env` files or secret files. Backing services read credentials from `*_FILE` variables where supported. |
| **Health checks** | Every service defines `healthcheck`. Dependents use `depends_on` with `condition: service_healthy`. |
| **Resource limits** | `deploy.resources.limits` on every service. |
| **Hardening** | Every service: `read_only: true` (plus `tmpfs` for scratch paths), `cap_drop: [ALL]`, `security_opt: [no-new-privileges:true]`, non-root `user`. Backing services that require a writable data directory get it only through a named volume. |
| **Restart policy** | `restart: unless-stopped` (production). |
| **Volumes** | Named volumes for PostgreSQL, Valkey (if persistence is enabled), and RabbitMQ data. Bind mounts to host paths only for source code in development. |
| **Network isolation** | Explicit networks. The `backend` network is `internal: true` in production; only the reverse proxy/API is attached to an external-facing network. |
| **Images** | Backing-service images pinned by digest in production; `latest` is forbidden. |
| **Logging driver** | Configure log rotation (`json-file` with `max-size`/`max-file`) or ship via the Collector. |
| **Backups** | Production volumes have a documented, tested backup and restore procedure (PostgreSQL via `pg_dump`/WAL archiving; RabbitMQ definitions exported). |
| **Parity with production** | Development topology mirrors production (same env var names, same service roles). |

```yaml
services:
    ecommerce-api:
        build:
            context: .
            dockerfile: Dockerfile
            target: runtime
        ports:
            - "127.0.0.1:8080:8080"
        environment:
            ASPNETCORE_ENVIRONMENT: Development
            ConnectionStrings__Postgres: "Host=postgres;Database=ecommerce;Username=app;Password=${POSTGRES_PASSWORD}"
            ConnectionStrings__Valkey: "valkey:6379"
            ConnectionStrings__RabbitMQ: "amqp://app:${RABBITMQ_PASSWORD}@rabbitmq:5672"
            OTEL_EXPORTER_OTLP_ENDPOINT: "http://otel-collector:4317"
        read_only: true
        tmpfs:
            - /tmp
        cap_drop: [ALL]
        security_opt:
            - no-new-privileges:true
        healthcheck:
            # App-provided probe mode: the chiseled runtime image ships no shell, curl, or wget
            test: ["CMD", "dotnet", "Portfolio.dll", "--health-check"]
            interval: 15s
            timeout: 5s
            retries: 3
            start_period: 10s
        deploy:
            resources:
                limits:
                    cpus: "1.0"
                    memory: 512M
        depends_on:
            postgres:
                condition: service_healthy
            valkey:
                condition: service_healthy
            rabbitmq:
                condition: service_healthy
        networks:
            - backend

    postgres:
        image: postgres:17-alpine@sha256:abc123...
        environment:
            POSTGRES_DB: ecommerce
            POSTGRES_USER: app
            POSTGRES_PASSWORD_FILE: /run/secrets/postgres_password
        secrets:
            - postgres_password # without this grant, /run/secrets/postgres_password does not exist in the container
        volumes:
            - pgdata:/var/lib/postgresql/data
        healthcheck:
            test: ["CMD-SHELL", "pg_isready -U app -d ecommerce"]
            interval: 10s
            timeout: 5s
            retries: 5
        deploy:
            resources:
                limits:
                    cpus: "0.5"
                    memory: 512M
        networks:
            - backend

    # valkey, rabbitmq, and otel-collector follow the same pattern: pinned image, healthcheck,
    # limits, named volume where state exists, and the backend network only.

volumes:
    pgdata:

networks:
    backend:
        driver: bridge

secrets:
    postgres_password:
        file: ./secrets/postgres_password.txt # local-only file, listed in .gitignore
```

</DockerComposeStandards>

---

<KubernetesWorkloadStandards>
## 6. Kubernetes Workload Standards

Kubernetes manifests live in `kubernetes/` (Kustomize base + per-environment overlays).

### 6.1 Pod Design

| Requirement | Mandatory Behavior |
| --- | --- |
| **One process per container** | Sidecars only for cross-cutting concerns (proxy, secrets injection). The OpenTelemetry Collector runs as a separate Deployment/DaemonSet. |
| **Process roles** | The same image can run as the **API** role (HTTP) and a **worker** role (RabbitMQ consumers, outbox relay, scheduled jobs), selected through configuration. Deploy them as separate Deployments so they scale and fail independently. |
| **Security context on every Pod** | Full security context from [Section 3.1](#31-security-context). |
| **No `hostPID`, `hostIPC`, `hostNetwork`** | Forbidden in production. |
| **Disable SA token auto-mount** | `automountServiceAccountToken: false` — the application never calls the Kubernetes API. |
| **Pod anti-affinity for HA** | Spread replicas across nodes and zones. |
| **Labels** | `app.kubernetes.io/name`, `/version`, `/component` (`api` or `worker`), `/managed-by`. |

```yaml
apiVersion: v1
kind: Pod
metadata:
    labels:
        app.kubernetes.io/name: ecommerce-api
        app.kubernetes.io/version: "1.4.2"
        app.kubernetes.io/component: api
        app.kubernetes.io/managed-by: kustomize
spec:
    automountServiceAccountToken: false
    securityContext:
        runAsNonRoot: true
        runAsUser: 1654 # the .NET image's non-root "app" user
        runAsGroup: 1654
        fsGroup: 1654
        seccompProfile:
            type: RuntimeDefault
    containers:
        - name: ecommerce-api
          image: registry.example.com/ecommerce-api:1.4.2@sha256:abc123...
          securityContext:
              allowPrivilegeEscalation: false
              readOnlyRootFilesystem: true
              capabilities:
                  drop: ["ALL"]
          env:
              - name: DOTNET_EnableDiagnostics_IPC
                value: "0" # no diagnostic socket in production; enable only for a debugging session in staging
          resources:
              requests:
                  cpu: "250m"
                  memory: "256Mi"
              limits:
                  cpu: "1000m"
                  memory: "512Mi"
          ports:
              - name: http
                containerPort: 8080
                protocol: TCP
          livenessProbe:
              httpGet:
                  path: /health/live
                  port: http
              periodSeconds: 15
              failureThreshold: 3
          readinessProbe:
              httpGet:
                  path: /health/ready
                  port: http
              periodSeconds: 10
              failureThreshold: 3
          startupProbe:
              httpGet:
                  path: /health/live
                  port: http
              periodSeconds: 5
              failureThreshold: 30
          volumeMounts:
              - name: tmp
                mountPath: /tmp
    volumes:
        - name: tmp
          emptyDir:
              medium: Memory
              sizeLimit: 64Mi
```

### 6.2 Deployment Strategy

| Requirement | Mandatory Behavior |
| --- | --- |
| **Rolling update by default** | `RollingUpdate` with `maxUnavailable: 0`, `maxSurge: 1` (or 25%). |
| **Minimum replicas** | Production `replicas >= 2` for the API. |
| **Revision history** | `revisionHistoryLimit >= 5`. |
| **Progress deadline** | Set `progressDeadlineSeconds`. |
| **Canary / blue-green** | For critical changes (checkout, billing), use Argo Rollouts or Flagger with automated analysis. |
| **Image pull policy** | `IfNotPresent` for tagged/digest-pinned images. |
| **Schema compatibility** | Rolling updates require that the old and new application versions both work against the current schema ([DATABASE.md Section 6](./DATABASE.md) — expand/contract migrations). |

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
    name: ecommerce-api
spec:
    replicas: 3
    revisionHistoryLimit: 10
    progressDeadlineSeconds: 300
    strategy:
        type: RollingUpdate
        rollingUpdate:
            maxUnavailable: 0
            maxSurge: 1
    selector:
        matchLabels:
            app.kubernetes.io/name: ecommerce-api
    template:
        # ... Pod spec as above
```

### 6.3 Service and Ingress

| Requirement | Mandatory Behavior |
| --- | --- |
| **ClusterIP by default** | `LoadBalancer`/`NodePort` only when required. |
| **Named ports** | `http` (and `metrics` if a scrape endpoint is enabled). |
| **Ingress TLS** | All Ingress resources configure TLS, with cert-manager for certificates. |
| **Rate limiting and WAF** | Apply at the ingress controller for public endpoints, in addition to application-level rate limiting ([SECURITY.md](./SECURITY.md)). |
| **Scalar UI and OpenAPI not exposed** | `/api/v1/docs` and `/api/v1/openapi/v1.json` are enabled in `Development` only; the Ingress must not route them in staging/production unless explicitly authorized. |
| **Path/host routing** | Avoid wildcard rules that expose unintended services. |
| **Annotations** | Set timeouts, body-size limits, and connection limits explicitly. |

### 6.4 ConfigMaps and Secrets

| Requirement | Mandatory Behavior |
| --- | --- |
| **ConfigMaps for non-sensitive data** | Feature flags, log levels, timeouts. |
| **External Secrets for sensitive data** | External Secrets Operator, Sealed Secrets, or Vault Agent Injector. Never commit plain Secret YAML. Connection strings for PostgreSQL, Valkey, and RabbitMQ are secrets. |
| **Mount as files** | Prefer secret files on `tmpfs` (read with the `*_FILE`/`AddKeyPerFile` configuration provider) over environment variables. |
| **Immutable objects** | `immutable: true` for ConfigMaps/Secrets in production. |
| **Versioned naming** | Hash-suffixed names (Kustomize generators do this) so changes trigger a rollout. |

### 6.5 Autoscaling

| Requirement | Mandatory Behavior |
| --- | --- |
| **HPA for the API** | CPU plus a request-rate or latency metric. |
| **KEDA for workers** | Scale RabbitMQ consumers on queue depth (KEDA `rabbitmq` scaler), with a `maxReplicaCount` that does not exceed the PostgreSQL connection budget. |
| **Bounds** | `minReplicas` for availability; `maxReplicas` for budget and database capacity. |
| **Scale-down stabilization** | `behavior.scaleDown.stabilizationWindowSeconds >= 300`. |
| **VPA for right-sizing** | Recommendation mode only. |

```yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
    name: ecommerce-api
spec:
    scaleTargetRef:
        apiVersion: apps/v1
        kind: Deployment
        name: ecommerce-api
    minReplicas: 2
    maxReplicas: 10
    behavior:
        scaleDown:
            stabilizationWindowSeconds: 300
            policies:
                - type: Percent
                  value: 10
                  periodSeconds: 60
        scaleUp:
            stabilizationWindowSeconds: 0
            policies:
                - type: Percent
                  value: 100
                  periodSeconds: 30
    metrics:
        - type: Resource
          resource:
              name: cpu
              target:
                  type: Utilization
                  averageUtilization: 70
```

### 6.6 Pod Disruption Budgets

| Requirement | Mandatory Behavior |
| --- | --- |
| **PDB for production workloads** | Every production Deployment with `replicas >= 2` has a PodDisruptionBudget. |
| **Budget** | `minAvailable >= 50%` or `maxUnavailable <= 1`. |
| **Coordinate with drains** | Compatible with the node maintenance strategy. |

```yaml
apiVersion: policy/v1
kind: PodDisruptionBudget
metadata:
    name: ecommerce-api
spec:
    minAvailable: "50%"
    selector:
        matchLabels:
            app.kubernetes.io/name: ecommerce-api
```

### 6.7 Jobs and CronJobs

| Requirement | Mandatory Behavior |
| --- | --- |
| **Idempotent execution** | Jobs are safe to re-run (at-least-once) — see the idempotency rule in [AGENTS.md](../AGENTS.md). |
| **`activeDeadlineSeconds`** | Set on every Job. |
| **`ttlSecondsAfterFinished`** | Set to clean up finished Pods. |
| **`concurrencyPolicy`** | `Forbid` or `Replace` for CronJobs. |
| **`startingDeadlineSeconds`** | Set for CronJobs. |
| **Backoff limit** | `backoffLimit` of 3–6. |
| **Database migrations** | EF Core migrations run as a dedicated Job (or init step) using a **migrations bundle** (`dotnet ef migrations bundle`) built in CI with its own least-privilege-but-DDL-capable credentials — never automatically at application startup in production ([DATABASE.md Section 6](./DATABASE.md)). The Job completes before the new Deployment rolls out. |

</KubernetesWorkloadStandards>

---

<KubernetesClusterOperations>
## 7. Kubernetes Cluster Operations

Managed Kubernetes is preferred; cluster-level items below are owned by the cluster provider where managed.

### 7.1 Namespace Strategy

| Requirement | Mandatory Behavior |
| --- | --- |
| **Namespace per environment** | `ecommerce-dev`, `ecommerce-staging`, `ecommerce-production` (production preferably in a separate cluster). |
| **No default namespace** | Never deploy to `default`. |
| **Labels** | `environment`, `team`, and `cost-center`, plus the Pod Security labels. |

### 7.2 Resource Quotas and Limit Ranges

| Requirement | Mandatory Behavior |
| --- | --- |
| **ResourceQuota per namespace** | Aggregate CPU, memory, and object-count limits. |
| **LimitRange per namespace** | Defaults and min/max to catch missing declarations. |
| **Regular review** | Review quotas quarterly against actual usage. |

```yaml
apiVersion: v1
kind: LimitRange
metadata:
    name: default-limits
    namespace: ecommerce-production
spec:
    limits:
        - type: Container
          default:
              cpu: "500m"
              memory: "256Mi"
          defaultRequest:
              cpu: "100m"
              memory: "128Mi"
          max:
              cpu: "2000m"
              memory: "2Gi"
          min:
              cpu: "50m"
              memory: "64Mi"
```

### 7.3 Network Policies

| Requirement | Mandatory Behavior |
| --- | --- |
| **Default deny all** | Default-deny `NetworkPolicy` for ingress **and** egress in every production namespace. |
| **Explicit allow rules** | Allow only: ingress controller → API on 8080; API/worker → PostgreSQL 5432, Valkey 6379, RabbitMQ 5672; API/worker → OpenTelemetry Collector OTLP (4317/4318); DNS; and specific egress to payment, carrier, and e-mail/SMS provider endpoints. |
| **Block metadata endpoint** | Block egress to `169.254.169.254` (and the IPv6 equivalent) from application workloads. |
| **Egress restrictions** | Allowlisted CIDRs or FQDN-based policies for external providers. |
| **mTLS (optional)** | A service mesh is optional for this single-deployable topology; use TLS to backing services instead unless a mesh is adopted via ADR. |

### 7.4 Storage and Stateful Dependencies

| Requirement | Mandatory Behavior |
| --- | --- |
| **Prefer managed backing services** | In production, prefer managed PostgreSQL, Valkey, and RabbitMQ. If self-hosted on Kubernetes, use operators (CloudNativePG for PostgreSQL, RabbitMQ Cluster Operator) and StatefulSets with PVCs — never a plain Deployment for a stateful service. |
| **StorageClass per tier** | SSD-backed class for PostgreSQL and RabbitMQ; Valkey persistence only if the use case requires it (it is a cache by default — [DATABASE.md](./DATABASE.md)). |
| **PVC reclaim policy** | `Retain` for production data. |
| **Volume encryption** | Encryption at rest for all PersistentVolumes. |
| **Backup and restore** | PostgreSQL has continuous WAL archiving / scheduled backups with point-in-time recovery; RabbitMQ definitions are backed up; restores are tested at least quarterly. |

### 7.5 Cluster Maintenance and Upgrades

| Requirement | Mandatory Behavior |
| --- | --- |
| **Version currency** | Run a supported Kubernetes version (N, N-1, or N-2). |
| **Patch within SLA** | `CRITICAL` CVEs within 72 hours; `MEDIUM`+ within 30 days. |
| **Rolling node upgrades** | Cordon, drain, upgrade, uncordon — one node at a time; PDBs respected. |
| **Pre-upgrade validation** | Validate in staging before production. |

</KubernetesClusterOperations>

---

<HealthChecksAndResilience>
## 8. Health Checks and Resilience

### 8.1 Probe Design

Every production container implements all three Kubernetes probes:

| Probe | Purpose | Mandatory Configuration |
| --- | --- | --- |
| **Liveness** | Detects a deadlocked or irrecoverable process | Lightweight, no I/O. `failureThreshold >= 3`. |
| **Readiness** | Gates traffic until the container can serve | May check PostgreSQL, Valkey, and RabbitMQ connectivity. |
| **Startup** | Gives slow-starting containers time to boot | `failureThreshold * periodSeconds` covers worst-case startup. |

| Rule | Mandatory Behavior |
| --- | --- |
| **Separate endpoints** | `/health/live` (liveness, no dependency checks) and `/health/ready` (readiness, dependency checks tagged `ready`). |
| **No heavy operations in liveness** | Never query the database or external APIs in liveness. |
| **Valkey is a soft dependency** | The cache is an optimization: a Valkey outage degrades readiness checks to a warning (`Degraded`), not `Unhealthy`, unless a flow truly depends on it. PostgreSQL is a hard dependency. |
| **Health endpoints are not public** | `/health/*` is reachable only by the orchestrator/internal network, not through the public Ingress. |
| **Consistent timeouts** | `timeoutSeconds` above the endpoint's p99 latency. |

### 8.2 Graceful Shutdown

| Requirement | Mandatory Behavior |
| --- | --- |
| **Handle SIGTERM** | ASP.NET Core's generic host traps `SIGTERM` and begins shutdown; keep `ENTRYPOINT` in exec form so the signal reaches the process. |
| **`terminationGracePeriodSeconds`** | Larger than `HostOptions.ShutdownTimeout` plus the `preStop` delay. |
| **`preStop` hook** | Native `sleep` action (Kubernetes 1.30+) so the Service deregisters the Pod first; no shell needed. |
| **Connection draining** | Stop accepting new requests, finish in-flight ones. |
| **Readiness fails during drain** | On `SIGTERM`, readiness returns `Unhealthy` immediately while in-flight work completes. |
| **Consumers stop cleanly** | RabbitMQ consumers stop fetching (cancel consumer), finish and acknowledge in-flight messages (or let them be redelivered — consumers are idempotent), and the outbox relay completes its current batch. |
| **Exit code** | `0` on clean shutdown. |

```yaml
# container level
lifecycle:
    preStop:
        sleep:
            seconds: 5 # native sleep action; no /bin/sh required
# Pod level
terminationGracePeriodSeconds: 60
```

### 8.3 Restart Policies and Back-Off

| Requirement | Mandatory Behavior |
| --- | --- |
| **`restartPolicy`** | `Always` for Deployments; `OnFailure` or `Never` for Jobs. |
| **CrashLoopBackOff awareness** | A crash-looping container indicates a configuration or dependency issue (e.g., unreachable PostgreSQL at startup); investigate rather than retry indefinitely. |
| **Circuit breakers for dependencies** | Use `Microsoft.Extensions.Resilience` (Polly v8) per [CODE.md Section 5](./CODE.md) to prevent cascading failures when an external dependency is unavailable. |

</HealthChecksAndResilience>

---

<PerformanceAndOptimization>
## 9. Performance and Optimization

### 9.1 Image Size Optimization

| Technique | Expected Outcome |
| --- | --- |
| **Chiseled base image** | Much smaller and with a smaller attack surface than a full OS image; faster pulls. |
| **Multi-stage builds** | Exclude the SDK and build tooling. |
| **BuildKit cache mounts** | NuGet caches never land in a layer. |
| **Trimming / ReadyToRun** | `PublishReadyToRun` improves startup; `PublishTrimmed` and Native AOT only with an ADR (EF Core and reflection-heavy libraries have constraints). |
| **Regular audits** | Periodically run `docker history` and `dive`. |

### 9.2 Container Startup Performance

| Technique | Mandatory Behavior |
| --- | --- |
| **Minimize initialization** | Defer non-critical work (cache warming) to background tasks after readiness. |
| **Startup probes** | Avoid premature liveness kills during slow initialization. |
| **Image layer caching** | Restore layer cached independently from code. |
| **ReadyToRun** | Enable for the production publish to cut JIT time. |

### 9.3 Runtime Performance

| Technique | Mandatory Behavior |
| --- | --- |
| **Right-sized resources** | Base requests on observed p95 usage. |
| **Runtime-aware limits** | The .NET GC honors the cgroup memory limit (default heap hard limit is 75% of it); tune with `DOTNET_GCHeapHardLimitPercent` only with profiling evidence. `DOTNET_gcServer` only when the CPU limit is ≥ 2 cores and measurements support it. |
| **Connection pooling** | Size the Npgsql pool and RabbitMQ channels against container concurrency and the PostgreSQL connection budget (`replicas × max pool size` must be below the server's limit; use PgBouncer if needed). |
| **Profile before tuning** | Use `dotnet-trace`/`dotnet-counters` in staging via an ephemeral debug container. |

### 9.4 Scheduling and Affinity

| Requirement | Mandatory Behavior |
| --- | --- |
| **Topology spread constraints** | Distribute Pods across availability zones. |
| **Pod anti-affinity** | Spread replicas across nodes. |
| **Priority classes** | Critical workloads (API, workers) have a PriorityClass so they are not preempted. |

</PerformanceAndOptimization>

---

<CICDIntegration>
## 10. CI/CD Integration

### 10.1 Build Pipeline

| Stage | Description | Blocking? |
| --- | --- | --- |
| **Lint Dockerfile** | `hadolint`. | Yes |
| **Build image** | Multi-stage build of the production image. | Yes |
| **Run unit tests** | `dotnet test` in a build/test stage. | Yes |
| **Scan for CVEs** | Trivy, Grype, Docker Scout, or Snyk Container; block on `HIGH`/`CRITICAL`. | Yes |
| **Generate SBOM** | SPDX or CycloneDX, attached to the image. | No |
| **Sign image** | Cosign or Notation, with SBOM/provenance attestations. | Yes |
| **Push to registry** | Semantic version and Git SHA tags. | Yes |

### 10.2 Deployment Pipeline

| Stage | Description | Blocking? |
| --- | --- | --- |
| **Manifest validation** | `kubeconform` (schema) and `kubectl --dry-run=server`; `kustomize build` succeeds for every overlay. Compose files: `docker compose config`. | Yes |
| **Policy check** | Kyverno, OPA/Gatekeeper, or Conftest policies against manifests. | Yes |
| **Run migrations** | The EF Core migrations bundle Job completes successfully ([Section 6.7](#67-jobs-and-cronjobs)). | Yes |
| **Deploy to staging** | Apply manifests; run smoke tests. | Yes |
| **Integration tests** | Against the staging deployment. | Yes |
| **Approval gate** | Manual or automated approval before production. | Yes |
| **Deploy to production** | Rolling, canary, or blue/green; monitor health. | Yes |
| **Post-deploy validation** | Smoke tests and synthetic monitors; verify error rate and latency. | Yes |

### 10.3 Rollback Strategy

| Requirement | Mandatory Behavior |
| --- | --- |
| **Automated rollback** | Triggered by error-rate thresholds, latency spikes, or failed health checks. |
| **Manual rollback procedure** | Documented and tested (`kubectl rollout undo`, or redeploying the previous digest with Compose). |
| **Revision retention** | `revisionHistoryLimit >= 5`. |
| **Database compatibility** | Migrations are expand/contract so the previous application version keeps working after rollback. |

</CICDIntegration>

---

<MonitoringAlertingDebugging>
## 11. Monitoring, Alerting, and Debugging

Telemetry design is owned by [OBSERVABILITY.md](./OBSERVABILITY.md); this section lists container-level requirements.

### 11.1 Metrics

| Metric Category | Examples |
| --- | --- |
| **Application (RED)** | Request rate, error rate, request duration (p50/p95/p99) |
| **Resource (USE)** | CPU, memory, GC pause/heap, thread-pool queue length, Npgsql pool usage |
| **Container** | Restart count, OOMKilled events, image pull latency |
| **Dependencies** | RabbitMQ queue depth and consumer lag, DLQ size, Valkey hit ratio, PostgreSQL connections |
| **Business** | `orders_placed_total`, `payments_failed_total`, `stock_reservations_expired_total` |

### 11.2 Alerting Rules

| Condition | Severity | Required Response Time |
| --- | --- | --- |
| Pod in `CrashLoopBackOff` > 5 minutes | Critical | < 15 minutes |
| Error rate > 5% for 5 minutes | Critical | < 15 minutes |
| RabbitMQ DLQ non-empty / queue depth growing for 10 minutes | Warning | Investigate within 1 hour |
| p99 latency > SLO threshold | Warning | Within 1 hour |
| Node `NotReady` > 5 minutes | Critical | < 15 minutes |
| PVC usage > 80% | Warning | Expand within 24 hours |
| Certificate expiry < 14 days | Warning | Rotate within 7 days |
| Image CVE `CRITICAL` in running Pod | Critical | Patch within 72 hours |
| HPA at `maxReplicas` for > 30 minutes | Warning | Review within 4 hours |

### 11.3 Debugging and Troubleshooting

| Practice | Mandatory Behavior |
| --- | --- |
| **Ephemeral debug containers** | `kubectl debug` with ephemeral containers; never install debug tools in production images. |
| **`kubectl exec` restrictions** | Audited and limited to on-call personnel via RBAC. |
| **Port-forward over expose** | Never create temporary Services/Ingress for debugging. |
| **Log aggregation** | Start from centralized logs and traces, not `kubectl logs` alone. |
| **Runbooks** | Runbooks for OOMKilled, CrashLoopBackOff, ImagePullBackOff, DLQ growth, and DB connection exhaustion; linked from alerts. |

</MonitoringAlertingDebugging>

---

<MultiEnvironment>
## 12. Multi-Environment Considerations

| Requirement | Mandatory Behavior |
| --- | --- |
| **Environment parity** | Staging mirrors production in architecture, configuration, and image versions; only scale and cost differ. |
| **Kustomize for environment differences** | `kubernetes/base` plus `kubernetes/overlays/{development,staging,production}`. Raw manifest duplication is forbidden. |
| **Portable manifests** | Keep cloud-specific annotations inside overlays. |
| **GitOps for deployment** | Argo CD or Flux reconciles from Git; manual `kubectl apply` in production is forbidden. |
| **Configuration drift detection** | Drift between declared and actual state is a defect. |
| **Compose production host** | The `docker-compose-prod.yml` host is managed as code ([IAC.md](./IAC.md)); changes are deployed by the pipeline, not by editing files on the host. |

</MultiEnvironment>

---

<DotNetContainerGuidance>
## 13. .NET Container Guidance

### 13.1 Base Images and Build Tooling

| Item | Standard |
| --- | --- |
| **Build stage** | `mcr.microsoft.com/dotnet/sdk:10.0-noble` (pinned by digest). |
| **Runtime base** | `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (framework-dependent). `runtime-deps:10.0-noble-chiseled` only for self-contained/Native AOT builds (ADR required). |
| **Build** | Multi-stage Dockerfile as in [Section 2.2](#22-multi-stage-builds), or SDK container publishing (`dotnet publish /t:PublishContainer`) if it yields the same hardening; `dotnet restore --locked-mode`. |
| **Runtime user** | The image's built-in non-root user (`$APP_UID`, 1654). |
| **Port** | 8080 (`ASPNETCORE_URLS=http://+:8080`); TLS terminates at the ingress/reverse proxy. |
| **Respecting limits** | The GC honors cgroup memory limits; set requests/limits deliberately ([Section 9.3](#93-runtime-performance)). |
| **Writable paths** | Only `/tmp` (`emptyDir`/`tmpfs`). ASP.NET Core Data Protection keys are persisted in PostgreSQL or another shared store (never the container filesystem) so multiple replicas share them. |
| **Environment** | `ASPNETCORE_ENVIRONMENT=Production` in staging and production; `DOTNET_EnableDiagnostics_IPC=0` in production; `DOTNET_TieredPGO` left at its default. |

### 13.2 Health, Shutdown, and Telemetry Libraries

| Concern | Library / approach |
| --- | --- |
| **Liveness / readiness endpoints** | `Microsoft.Extensions.Diagnostics.HealthChecks` with tag-filtered `MapHealthChecks("/health/live")` (no checks) and `MapHealthChecks("/health/ready")` (tag `ready`); `AspNetCore.HealthChecks.NpgSql`, `.Redis` (works with Valkey), and `.Rabbitmq` for dependency checks. |
| **`--health-check` probe mode (Compose)** | A small startup branch in `Program.cs` that calls the local `/health/ready` and exits `0`/`1`, used by the Compose `healthcheck`. |
| **Graceful shutdown** | `IHostApplicationLifetime`; `HostOptions.ShutdownTimeout` below `terminationGracePeriodSeconds`. |
| **Metrics and traces** | OpenTelemetry .NET (OTLP exporter) per [OBSERVABILITY.md Section 16](./OBSERVABILITY.md). |
| **Migrations** | `dotnet ef migrations bundle` built in CI and executed as a Job. |

</DotNetContainerGuidance>

---

<DefinitionOfDone>
## 14. Container Definition of Done

A delivery that involves container workloads is complete only when all items below are true:

1. Images use the minimal chiseled base from [Section 13.1](#131-base-images-and-build-tooling), pinned to digest. No floating tags.
2. The multi-stage build excludes all build-time tools from the production image.
3. The Dockerfile passes `hadolint` with no blocking warnings.
4. The image scan reports zero `HIGH` or `CRITICAL` CVEs.
5. The image is signed (and verified at admission on Kubernetes); an SBOM is generated and stored.
6. Containers run as non-root with a read-only root filesystem, dropped capabilities, and no privilege escalation — in Compose and Kubernetes alike.
7. Resource requests and limits are set on every container.
8. Backing services (PostgreSQL, Valkey, RabbitMQ) are not published outside the internal network in production.
9. Liveness, readiness, and startup probes (or Compose health checks) are implemented and tuned.
10. Graceful shutdown handles `SIGTERM`, drains in-flight requests, and stops consumers cleanly.
11. Kubernetes network policies enforce default-deny with explicit allow rules.
12. Secrets come from a secrets manager or Compose secrets. No plain secrets in Git, images, or `.env` files that are committed.
13. PodDisruptionBudgets exist for all production Deployments with `replicas >= 2`.
14. Autoscaling (HPA for the API, KEDA for workers) is configured for variable-load workloads.
15. Deployment uses rolling update, canary, or blue/green with an automated rollback path; migrations run as a separate step.
16. Dashboards and alerts cover RED metrics, resource usage, queue depth, and container health; runbooks are linked from alerts.
17. All manifests are deployed via CI/CD or GitOps. No manual changes in production.
18. Security requirements from [SECURITY.md](./SECURITY.md) are addressed for container-specific controls.

Any exception to these standards must be documented with owner, scope, risk, rationale, and expiration date.

</DefinitionOfDone>

---

_Last updated: September 30, 2026_
_These standards must be reviewed and updated at minimum once per year or after any significant container or Kubernetes incident._

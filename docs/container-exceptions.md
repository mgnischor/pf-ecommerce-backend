# Container Standards — Documented Exceptions

`ai/CONTAINERS.md` allows deviations only as documented exceptions with owner, scope, risk, rationale, and
expiration date. These are the deviations in `Dockerfile`, `docker-compose-dev.yml`, and `docker-compose-prod.yml`.

> **Owner** is a placeholder until the maintainers assign one (same convention as `docs/business-rules/`).
> Every exception expires on **2027-03-31** and must be re-justified or removed by then.

## EX-001 — No `healthcheck` for `otel-collector` and `loki` (production Compose)

| Field      | Value                                                                                                                                                                                                                                                                   |
| ---------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Standard   | `CONTAINERS.md §5`: every service defines a `healthcheck`                                                                                                                                                                                                               |
| Owner      | Maintainers (to be assigned)                                                                                                                                                                                                                                            |
| Scope      | `docker-compose-prod.yml`: services `otel-collector` and `loki`                                                                                                                                                                                                         |
| Risk       | Docker detects a crash (`restart: unless-stopped` recovers it) but not a process that is alive and hung. Dependents (`otel-collector`, `grafana`) use `condition: service_started` for Loki instead of `service_healthy`                                                |
| Rationale  | Both images are distroless: no shell, `curl`, or `wget`, and neither binary offers a probe command (`tempo` has `-health`, `prometheus` has `promtool`, `grafana` ships `wget`, so those do have one). Adding a shell or a sidecar would violate the minimal-image rule |
| Mitigation | Loki's `/ready` and the Collector's `health_check` extension (`:13133`) exist on the internal network for Kubernetes probes and external monitoring; telemetry loss never affects request handling (`OBSERVABILITY.md §20.11`)                                          |
| Expires    | 2027-03-31                                                                                                                                                                                                                                                              |

## EX-002 — `otel-lgtm` runs as root with a writable root filesystem (development Compose)

| Field     | Value                                                                                                                                                                                                |
| --------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Standard  | `CONTAINERS.md §3.1`, §5: non-root user, `read_only: true`                                                                                                                                           |
| Owner     | Maintainers (to be assigned)                                                                                                                                                                         |
| Scope     | `docker-compose-dev.yml`: service `otel-lgtm` only. Never deployed                                                                                                                                   |
| Risk      | Local only: every port is bound to `127.0.0.1`, capabilities are dropped, `no-new-privileges` is set, and resource limits apply                                                                      |
| Rationale | `grafana/otel-lgtm` bundles five processes that write to several paths under `/data`, `/tmp`, and its own tree; it is the all-in-one backend `OBSERVABILITY.md §18` prescribes for local development |
| Expires   | 2027-03-31                                                                                                                                                                                           |

## EX-003 — Grafana uses a local admin account instead of SSO (production Compose)

| Field     | Value                                                                                                                                                                                                                                                   |
| --------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Standard  | `OBSERVABILITY.md §13`: authenticate through OIDC; local accounts disabled except an audited break-glass account                                                                                                                                        |
| Owner     | Maintainers (to be assigned)                                                                                                                                                                                                                            |
| Scope     | `docker-compose-prod.yml`: service `grafana`                                                                                                                                                                                                            |
| Risk      | A single shared admin credential, no per-person audit trail                                                                                                                                                                                             |
| Rationale | No identity provider exists yet. Mitigations in place: random 128-bit password from a secret file, sign-up and anonymous access disabled, `Secure`/`SameSite=Strict` cookies, port bound to loopback so only the SSO-enforcing reverse proxy reaches it |
| Expires   | 2027-03-31                                                                                                                                                                                                                                              |

## EX-004 — No TLS between containers on the internal networks (production Compose)

| Field     | Value                                                                                                                                                                                                                                                                            |
| --------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Standard  | `CONTAINERS.md §3.4`: TLS for connections to PostgreSQL, Valkey, and RabbitMQ in production                                                                                                                                                                                      |
| Owner     | Maintainers (to be assigned)                                                                                                                                                                                                                                                     |
| Scope     | Traffic on the `backend` and `observability` Compose networks of one host                                                                                                                                                                                                        |
| Risk      | A process with access to the host's Docker network namespace could read traffic between containers                                                                                                                                                                               |
| Rationale | Both networks are `internal: true` (no route off the host) and hold no external client. `OBSERVABILITY.md §12.3` allows the Compose network or loopback in place of TLS for the single-host topology. Cross-host or Kubernetes deployments must use TLS (mTLS for the Collector) |
| Expires   | 2027-03-31                                                                                                                                                                                                                                                                       |

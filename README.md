# pf-ecommerce-backend

A complete e-commerce platform built as a portfolio project. Modular monolith in C# / .NET 10, organized with Domain-Driven Design (DDD) by bounded context.

## Overview

This repository implements the backend of a full-featured e-commerce system: product discovery, cart, checkout, ordering, billing, inventory, shipping, promotions, reviews, customer management, identity, and notifications.

Goals of this portfolio project:

- Show clean DDD modeling (aggregates, entities, value objects, domain events).
- Show a layered architecture with explicit dependency direction (`API → Application → Domain ← Infrastructure`).
- Show production-ready concerns: persistence, caching, messaging, and observability.

## Tech Stack

| Concern              | Technology                            |
| -------------------- | ------------------------------------- |
| Language / Runtime   | C# + .NET 10                          |
| Database             | PostgreSQL                            |
| .NET Postgres driver | Npgsql                                |
| ORM                  | Entity Framework Core                 |
| Cache                | Valkey                                |
| Messaging            | RabbitMQ                              |
| Observability        | OpenTelemetry (logs, metrics, traces) |
| API docs             | OpenAPI + Scalar                      |

## Bounded Contexts

All source code lives under `src/`, organized by bounded context (not by technical layer):

```
src/
├── Billing/
├── Cart/
├── Catalog/
├── Checkout/
├── Customers/
├── Identity/
├── Inventory/
├── Notifications/
├── Ordering/
├── Promotions/
├── Reviews/
├── Shipping/
└── SharedKernel/
```

Each bounded context follows the same internal layout:

```
src/Catalog/
├── Domain/
├── Application/
│   ├── Commands/
│   ├── Queries/
│   └── DTOs/
├── Infrastructure/
│   ├── Persistence/
│   ├── Messaging/
│   └── ExternalServices/
└── API/
    ├── Controllers/
    └── Contracts/
```

- `Domain` — aggregates, entities, value objects, domain events, repository interfaces. No infrastructure dependencies.
- `Application` — use cases (commands/queries), orchestration, DTOs.
- `Infrastructure` — EF Core persistence (Npgsql), RabbitMQ publishers/consumers, Valkey cache, external adapters.
- `API` — controllers and request/response contracts. No domain leakage.
- `SharedKernel` — minimal shared primitives only.

## Prerequisites

- .NET 10 SDK
- PostgreSQL 16+ (18 in Compose and in the tests)
- Docker (the integration tests start PostgreSQL with Testcontainers)
- Valkey 7+
- RabbitMQ 3+

## Getting Started

```bash
dotnet restore
dotnet build
dotnet run --project Portfolio.csproj
```

The API needs a PostgreSQL database: set `ConnectionStrings__Postgres` (or start the Compose stack, below). In
`Development` it applies its own migrations at start (`Database:MigrateOnStartup`); everywhere else a migrations bundle
does (`docs/database.md`).

Run the test suites (xUnit v3 on Microsoft.Testing.Platform, enabled in `global.json`):

```bash
dotnet test --project tests/Portfolio.UnitTests
dotnet test --project tests/Portfolio.ArchitectureTests
dotnet test --project tests/Portfolio.IntegrationTests
csharpier check .
```

API reference (development):

- Scalar UI: `https://localhost:<port>/api/v1/docs`
- OpenAPI document: `https://localhost:<port>/api/v1/openapi/v1.json`

## Configuration

Connection strings, endpoints, credentials, and feature flags are externalized via environment variables / user secrets. Never commit secrets.

| Setting                       | Description                                                                                                                                                               |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ConnectionStrings__Postgres` | PostgreSQL connection string (Npgsql). **Required**: the host refuses to start without it. Pool, timeout, and retry settings live under `Database:*` (`docs/database.md`) |
| `ConnectionStrings__Valkey`   | Valkey connection string                                                                                                                                                  |
| `ConnectionStrings__RabbitMQ` | RabbitMQ connection string                                                                                                                                                |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OpenTelemetry collector endpoint                                                                                                                                          |
| `AllowedHosts`                | Semicolon-separated host names the API answers to. Defaults to `localhost` (`*` only in `Development`); **set it to the real host names in every other environment**      |

Non-secret defaults live in `configuration/appsettings.json` and `configuration/appsettings.{Environment}.json`; the
composition root loads them explicitly (the default host only probes the content root). User secrets, environment
variables, and command-line arguments override them.

## Standards

Development standards live in the `ai/` folder and are indexed by `AGENTS.md`:

- `ai/ARCHITECTURE.md` — DDD, bounded contexts, layers, messaging, ADRs
- `ai/BUSINESS.md` — business rules, state machines, calculations, authorization
- `ai/CODE.md` — code quality, .NET 10 conventions, libraries, PR standards
- `ai/TESTS.md` — test pyramid, Testcontainers, contract and architecture tests, coverage
- `ai/DATABASE.md` — PostgreSQL, EF Core, Valkey caching, schema ownership, migrations
- `ai/SECURITY.md` — OWASP, abuse cases, cryptography, auth, secrets, hardening
- `ai/CONTAINERS.md` — Docker, Compose, Kubernetes
- `ai/IAC.md` — infrastructure as code (Terraform; AWS, Azure, Google Cloud, and OCI templates)
- `ai/OBSERVABILITY.md` — OpenTelemetry, SLOs, alerts
- `ai/API_CONTRACTS.md` — API contract and consumer-experience conventions (this repository has no UI)
- `ai/TASKS.md` — task checklist

Read the relevant document(s) before writing code, not after.

## Domain Model

Implemented so far (see `ai/TASKS.md` for full progress). Types are `internal` by default; the rules are documented in
`docs/business-rules/`.

- `SharedKernel` — `Entity` (traceability, type-aware identity equality), `AggregateRoot` (concurrency `Version` advanced
  once per state change, domain-event collection), `Result`/`Result<T>` with `Error`/`ErrorType` (machine-readable errors:
  code, field, rule ID, parameters — no prose; text is localized at the API boundary), `Money` (`decimal`, ISO 4217,
  centralized banker's rounding), `IDomainEvent` (event ID, aggregate ID **and version**, UTC timestamp),
  `IRepository`, `IUnitOfWork`, `DomainException`.
- `Catalog` — `Product` aggregate (`Sku`, `ProductStatus`, `IProductRepository`, `ProductErrors`) enforcing
  BR-CAT-001 (naming and description), BR-CAT-002 (positive price), BR-CAT-003 (Draft → Active → Discontinued),
  BR-CAT-004 (SKU format), with `ProductCreated`, `ProductPriceChanged`, and `ProductStatusChanged` domain events.
  The Application layer adds the use cases `CreateProductHandler` (BR-CAT-005, SKU uniqueness),
  `ChangeProductPriceHandler`, `ActivateProductHandler`, and `DiscontinueProductHandler`.
- `Inventory` — `InventoryItem` aggregate (one per SKU: `OnHand`, `Reserved`, derived `Available`) and the append-only
  `StockMovement` ledger entity, enforcing BR-INV-001 (stock level bounds), BR-INV-002 (adjustment constraints),
  BR-INV-003 (append-only ledger, idempotent retries), BR-INV-004 (available stock), BR-INV-005/006 (reserve and
  release within limits), and BR-INV-007 (SKU format), with `InventoryItemOpened`, `StockAdjusted`, `StockReserved`,
  and `StockReleased` events. The Application layer adds `OpenInventoryItemHandler` (BR-INV-008),
  `GetInventoryItemHandler`, and `AdjustStockHandler`; the three endpoints are wired and persisted in PostgreSQL (schema `inventory`).

## API

Thirty-five versioned endpoints are mapped under `/api/v1` (plus the JWKS at `/.well-known/jwks.json`): one controller
per resource in each context's `API/Controllers`, request/response contracts in `API/Contracts`. Runnable examples
for all of them, including the sign-in flow, are in `Portfolio.http`; the OpenAPI document and Scalar UI
(development only) describe every operation.

| Context   | Endpoints                                                                                                                                                                                                                                  |
| --------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Identity  | `POST /auth/tokens`, `POST /auth/tokens/refresh`, `POST /auth/tokens/revocation`, `POST /auth/registrations`, `GET /auth/me`, `POST /users`, `PUT /users/{id}/access-level`, `POST /users/{id}/deactivation`, `GET /.well-known/jwks.json` |
| Catalog   | `GET /products`, `GET /products/{id}`, `POST /products`, `PATCH /products/{id}`, `PUT /products/{id}/price`, `POST /products/{id}/activation`, `POST /products/{id}/discontinuation`, `DELETE /products/{id}`                              |
| Cart      | `POST /carts`, `GET /carts/{id}`, `POST /carts/{id}/items`, `DELETE /carts/{id}/items/{itemId}`                                                                                                                                            |
| Checkout  | `POST /carts/{id}/checkout`, `GET /checkouts/{id}`                                                                                                                                                                                         |
| Ordering  | `GET /orders`, `GET /orders/{id}`, `POST /orders/{id}/cancellation`                                                                                                                                                                        |
| Billing   | `GET /payments/{id}`, `POST /payments/{id}/refunds`, `POST /payments/webhooks/{provider}`                                                                                                                                                  |
| Inventory | `POST /inventory/items`, `GET /inventory/items/{sku}`, `POST /inventory/items/{sku}/adjustments`                                                                                                                                           |
| Shipping  | `GET /orders/{id}/shipments`                                                                                                                                                                                                               |
| Customers | `GET /customers/me`                                                                                                                                                                                                                        |
| Platform  | `GET /diagnostics/runtime`                                                                                                                                                                                                                 |

Conventions already in place: cursor pagination (`limit`, `cursor`), `Idempotency-Key` on retried unsafe requests,
`If-Match` preconditions, camelCase JSON with string enums, money as decimal strings, RFC 9457 Problem Details, and
default-deny authorization (`[AllowAnonymous]` is explicit).

**Status:** authentication, account administration, and the Inventory endpoints are implemented. The other business
endpoints are mapped, validated, and protected, but their use cases are not wired yet, so they answer `501` with
Problem Details (`code: ENDPOINT_NOT_IMPLEMENTED`) once authorization passes.

## Authentication and authorization

JWT bearer authentication with short-lived ES384 access tokens and rotating refresh tokens, and five hierarchical
access levels: **public**, **collaborator**, **manager**, **administrator**, **developer**. Every endpoint either is
explicitly anonymous or names the minimum level it needs; an architecture test fails the build otherwise, and an
integration test calls every protected endpoint as anonymous and as each level. The full rules, the endpoint matrix,
the token specification, and how to operate the secrets are in `docs/business-rules/identity.md`.

| Setting (secret)                                                           | Description                                                                               |
| -------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------- |
| `Jwt__ActiveKeyId`, `Jwt__Keys__0__Id`, `Jwt__Keys__0__PrivateKeyPem`      | ES384 signing key (PKCS#8 PEM). Use `…__PrivateKeyPemFile` for a mounted file             |
| `Identity__TokenHashKey`                                                   | Base64 key of at least 32 random bytes; refresh tokens are stored only as HMAC-SHA3-512   |
| `Identity__Bootstrap__Accounts__0__Email`, `…__Password`, `…__AccessLevel` | Accounts created at startup when missing: how the first administrator and developer exist |

Outside `Development` the host refuses to start without the signing key and the token-hash key. In `Development` it
generates throw-away keys, so `dotnet run` works, but tokens stop validating on restart.

## Containers

| Piece                                                                                    | File                                                  |
| ---------------------------------------------------------------------------------------- | ----------------------------------------------------- |
| Production image (multi-stage, chiseled, non-root, digest-pinned)                        | `Dockerfile`                                          |
| Development stack: API, PostgreSQL, Valkey, RabbitMQ, all-in-one telemetry (`otel-lgtm`) | `docker-compose-dev.yml`                              |
| Single-host production stack: the above plus Collector, Prometheus, Loki, Tempo, Grafana | `docker-compose-prod.yml`                             |
| Collector, Prometheus, Loki, Tempo, and Grafana configuration                            | `observability/`                                      |
| Credential generators                                                                    | `scripts/init-secrets.sh`, `scripts/init-secrets.ps1` |
| Documented deviations from `ai/CONTAINERS.md`                                            | `docs/container-exceptions.md`                        |

Pinned versions (latest stable at the time of writing, each by tag **and** digest; Renovate/Dependabot update both):

| Component          | Version             | Component                      | Version |
| ------------------ | ------------------- | ------------------------------ | ------- |
| .NET SDK           | 10.0.401            | Prometheus                     | v3.15.0 |
| ASP.NET (chiseled) | 10.0.12             | Loki                           | 3.7.8   |
| PostgreSQL         | 18.6 (alpine 3.24)  | Tempo                          | 3.1.0   |
| Valkey             | 9.1.2 (alpine 3.24) | Grafana                        | 13.2.3  |
| RabbitMQ           | 4.3.6 (alpine)      | OTel Collector (contrib)       | 0.161.0 |
|                    |                     | `grafana/otel-lgtm` (dev only) | 0.34.0  |

**Development.** Generate credentials once (random values, untracked `.env`) and start the stack:

```bash
bash scripts/init-secrets.sh dev          # Windows: pwsh scripts/init-secrets.ps1 -Mode dev
docker compose -f docker-compose-dev.yml up --build
```

The API is on `http://localhost:8080` (Scalar at `/api/v1/docs`), Grafana on `:3000`, the RabbitMQ UI on `:15672`. Every
published port is bound to `127.0.0.1` and can be moved in `.env` (`API_PORT`, `POSTGRES_PORT`, `VALKEY_PORT`,
`RABBITMQ_PORT`, `RABBITMQ_MANAGEMENT_PORT`, `GRAFANA_PORT`) when it clashes with something already running, such as a
local PostgreSQL or RabbitMQ. The `.env` account (`DEV_ACCOUNT_EMAIL`) is created with the `developer` level.

**Production (single host).** Secrets are files, never environment variables:

```bash
bash scripts/init-secrets.sh prod --admin-email admin@example.com   # writes ./secrets/*
cp .env.prod.example .env.prod                                      # API_IMAGE, MIGRATIONS_IMAGE, API_ALLOWED_HOSTS, JWT_ISSUER
docker compose --env-file .env.prod -f docker-compose-prod.yml up -d
```

Before the API starts, one-shot jobs provision the database roles and schemas, run one EF Core migrations bundle per
bounded context as `app_migrator`, and grant the runtime role; the API then connects as `app_runtime`, which cannot
change the schema (`docs/database.md`, "Deployment"). `MIGRATIONS_IMAGE` is built from the Dockerfile target `migrations`.

`--env-file` keeps the development `.env` out of the production stack. TLS terminates at an external reverse proxy
that forwards to the API port; it must never route `/health/*` or the observability backends to the internet. Only the
API and Grafana publish a port (loopback by default); `backend` and `observability` are internal networks.

Every container runs non-root with a read-only root filesystem, all capabilities dropped, `no-new-privileges`, and CPU,
memory, and PID limits. The API healthcheck is the application itself (`dotnet Portfolio.dll --health-check`, which calls
`/health/ready`), because the chiseled image has no shell or `curl`. On `SIGTERM` readiness fails immediately, in-flight
requests drain within 25 s, and Docker waits 35 s before killing the container.

Known limitations: the application emits no OpenTelemetry yet (the pipeline is verified end to end with a synthetic
OTLP record, including redaction), readiness checks PostgreSQL but not Valkey/RabbitMQ until those integrations exist,
and ASP.NET Core Data Protection logs a warning about its in-memory key ring because nothing persists keys yet.

## Quality Gates

Defined by `ai/CODE.md §2` and enforced locally and in `.github/workflows/ci.yml`:

- `global.json` pins the SDK; `Directory.Build.props` turns warnings into errors with analyzers at `latest-recommended`
  plus SonarAnalyzer, Roslynator, Meziantou, and banned APIs (`BannedSymbols.txt`: no `DateTime.UtcNow`, `Thread.Sleep`,
  `new Random()`).
- `Directory.Packages.props` centralizes package versions; `packages.lock.json` files are committed and restored with
  `--locked-mode` in CI.
- Tests live under `tests/`: `Portfolio.UnitTests` (domain and application, no infrastructure),
  `Portfolio.ArchitectureTests` (dependency flow, context boundaries, naming, visibility, explicit endpoint
  authorization, persistence rules), and `Portfolio.IntegrationTests` (in-process HTTP pipeline on a real PostgreSQL in a
  Testcontainers container: migrations, constraints, concurrency, outbox/inbox, least-privilege roles, JWT validation,
  authorization matrix; Valkey and RabbitMQ join when their infrastructure lands).
- CI also checks that the migrations match the model (`dotnet ef migrations has-pending-model-changes`) and builds the
  migrations bundles and image.

## Roadmap

- [x] SharedKernel primitives (`Entity`, `AggregateRoot`, `Result`, `Money`, event abstractions)
- [~] Domain aggregates per bounded context (Catalog `Product` done, with its application use cases; remaining contexts pending)
- [x] Quality baseline (analyzers as errors, central packages, lock files, CI) and architecture fitness functions
- [x] EF Core + PostgreSQL persistence for Identity, Catalog, and Inventory: schema and `DbContext` per context, versioned migrations and bundles, traceability, soft delete, concurrency tokens, least-privilege roles, outbox, relay, inbox (`docs/database.md`)
- [ ] RabbitMQ publisher for the outbox relay and the first inbox consumer
- [ ] Valkey caching with explicit TTL/invalidation
- [ ] OpenTelemetry instrumentation in the application (the Collector/Prometheus/Loki/Tempo/Grafana stack is ready)
- [x] API surface mapped (34 endpoints, contracts, OpenAPI, `Portfolio.http`)
- [x] JWT authentication, refresh-token rotation, five access levels, and endpoint protection
- [x] Dockerfile and Compose stacks (development and production) with pinned versions
- [ ] Wire business endpoints to use cases; MFA for staff; `Idempotency-Key` storage

## License

See `LICENSE.md`.

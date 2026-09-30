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
- PostgreSQL 16+
- Valkey 7+
- RabbitMQ 3+

## Getting Started

```bash
dotnet restore
dotnet build
dotnet run --project Portfolio.csproj
```

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

| Setting                       | Description                                                                                                                                                          |
| ----------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ConnectionStrings__Postgres` | PostgreSQL connection string (Npgsql)                                                                                                                                |
| `ConnectionStrings__Valkey`   | Valkey connection string                                                                                                                                             |
| `ConnectionStrings__RabbitMQ` | RabbitMQ connection string                                                                                                                                           |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OpenTelemetry collector endpoint                                                                                                                                     |
| `AllowedHosts`                | Semicolon-separated host names the API answers to. Defaults to `localhost` (`*` only in `Development`); **set it to the real host names in every other environment** |

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

## Quality Gates

Defined by `ai/CODE.md §2` and enforced locally and in `.github/workflows/ci.yml`:

- `global.json` pins the SDK; `Directory.Build.props` turns warnings into errors with analyzers at `latest-recommended`
  plus SonarAnalyzer, Roslynator, Meziantou, and banned APIs (`BannedSymbols.txt`: no `DateTime.UtcNow`, `Thread.Sleep`,
  `new Random()`).
- `Directory.Packages.props` centralizes package versions; `packages.lock.json` files are committed and restored with
  `--locked-mode` in CI.
- Tests live under `tests/`: `Portfolio.UnitTests` (domain and application, no infrastructure),
  `Portfolio.ArchitectureTests` (dependency flow, context boundaries, naming, visibility), and
  `Portfolio.IntegrationTests` (in-process HTTP pipeline; Testcontainers join when PostgreSQL, Valkey, and RabbitMQ
  infrastructure lands).

## Roadmap

- [x] SharedKernel primitives (`Entity`, `AggregateRoot`, `Result`, `Money`, event abstractions)
- [~] Domain aggregates per bounded context (Catalog `Product` done, with its application use cases; remaining contexts pending)
- [x] Quality baseline (analyzers as errors, central packages, lock files, CI) and architecture fitness functions
- [ ] EF Core mappings + versioned migrations
- [ ] Transactional outbox + RabbitMQ integration events
- [ ] Valkey caching with explicit TTL/invalidation
- [ ] OpenTelemetry instrumentation (traces/metrics/logs)
- [ ] API endpoints, Problem Details mapping, authentication, and `Idempotency-Key`

## License

See `LICENSE.md`.

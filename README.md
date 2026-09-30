# pf-ecommerce-backend

A complete e-commerce platform built as a portfolio project. Modular monolith in C# / .NET 10, organized with Domain-Driven Design (DDD) by bounded context.

## Overview

This repository implements the backend of a full-featured e-commerce system: product discovery, cart, checkout, ordering, billing, inventory, shipping, promotions, reviews, customer management, identity, and notifications.

Goals of this portfolio project:

- Show clean DDD modeling (aggregates, entities, value objects, domain events).
- Show a layered architecture with explicit dependency direction (`API → Application → Domain ← Infrastructure`).
- Show production-ready concerns: persistence, caching, messaging, and observability.

## Tech Stack

| Concern | Technology |
| --- | --- |
| Language / Runtime | C# + .NET 10 |
| Database | PostgreSQL |
| .NET Postgres driver | Npgsql |
| ORM | Entity Framework Core |
| Cache | Valkey |
| Messaging | RabbitMQ |
| Observability | OpenTelemetry (logs, metrics, traces) |
| API docs | OpenAPI + Scalar |

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

API reference (development):

- Scalar UI: `https://localhost:<port>/api/v1/docs`
- OpenAPI document: `https://localhost:<port>/api/v1/openapi/v1.json`

## Configuration

Connection strings, endpoints, credentials, and feature flags are externalized via environment variables / user secrets. Never commit secrets.

| Setting | Description |
| --- | --- |
| `ConnectionStrings__Postgres` | PostgreSQL connection string (Npgsql) |
| `ConnectionStrings__Valkey` | Valkey connection string |
| `ConnectionStrings__RabbitMQ` | RabbitMQ connection string |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OpenTelemetry collector endpoint |

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

## Roadmap

- [ ] Domain aggregates per bounded context
- [ ] EF Core mappings + versioned migrations
- [ ] Transactional outbox + RabbitMQ integration events
- [ ] Valkey caching with explicit TTL/invalidation
- [ ] OpenTelemetry instrumentation (traces/metrics/logs)
- [ ] Architecture fitness functions in CI

## License

See `LICENSE.md`.

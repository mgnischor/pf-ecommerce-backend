# AGENTS.md

> This file orients any AI coding agent (and any human) working in this repository. It does not replace the standards documents — it tells you which one governs the task in front of you, and lists the rules that are load-bearing across all of them. When this file and a standards document disagree, the standards document wins; open an issue rather than guessing.

---

## 0. The project in one paragraph

`pf-ecommerce-backend` is the backend of a full e-commerce platform: a **modular monolith in C# / .NET 10**, organized with **Domain-Driven Design** by bounded context (`src/Billing`, `Cart`, `Catalog`, `Checkout`, `Customers`, `Identity`, `Inventory`, `Notifications`, `Ordering`, `Promotions`, `Reviews`, `Shipping`, `SharedKernel`). Each context has `Domain`, `Application`, `Infrastructure`, and `API` layers with the dependency direction `API → Application → Domain ← Infrastructure`.

| Concern | Technology |
| --- | --- |
| Language / runtime | C# + .NET 10 (ASP.NET Core, Kestrel) |
| Database | PostgreSQL via Entity Framework Core + Npgsql |
| Cache | Valkey |
| Messaging | RabbitMQ (transactional outbox) |
| Observability | OpenTelemetry (logs, metrics, traces) over OTLP |
| API documentation | OpenAPI + Scalar |
| Packaging and runtime | Docker (`Dockerfile`), Docker Compose (development and production), Kubernetes (`kubernetes/`) |

There is **no user interface** in this repository; it is an HTTP API consumed by other clients.

---

## 1. The standards suite

All guidance documents live in the `ai/` folder. Read the relevant one(s) before writing code, not after. `ai/TASKS.md` tracks task progress.

| Document | Governs | Read it when you are about to... |
| --- | --- | --- |
| **ai/ARCHITECTURE.md** | DDD structure, bounded contexts, layers, SOLID at the architectural level, messaging libraries, fitness tests, ADRs | Design a context, an aggregate, a module boundary, or an integration |
| **ai/BUSINESS.md** | Business rule classification and IDs (`BR-`), domain modeling from the business side, validation, state machines, calculations, authorization, temporal rules | Implement a business rule, a calculation, a workflow, or an access rule |
| **ai/CODE.md** | Quality gates, .NET 10 / C# 14 rules, performance rules, recommended libraries, commercial-license watchlist, PR standards | Write or review any code |
| **ai/TESTS.md** | Test pyramid, test project layout, xUnit/Testcontainers patterns, contract and architecture tests, coverage gates, flaky-test policy | Write any test, or decide what kind of test a change needs |
| **ai/DATABASE.md** | PostgreSQL, EF Core, Npgsql, Valkey caching, schema-per-context ownership, migrations, outbox/inbox, data governance | Change a schema, write a query or a mapping, add a migration, or cache something |
| **ai/SECURITY.md** | OWASP Top 10:2025 and API Top 10, e-commerce abuse cases, cryptography and approved libraries, JWT/OAuth2, secrets, HTTP hardening, supply chain, logging, incident response | Touch auth, crypto, secrets, payments, logging, or anything internet-facing |
| **ai/OBSERVABILITY.md** | OpenTelemetry instrumentation, logs/metrics/traces schema, Collector pipeline, SLOs and burn-rate alerts, dashboards, telemetry privacy | Add telemetry to a code path, define an SLO or alert, or change the observability stack |
| **ai/CONTAINERS.md** | Dockerfile, Docker Compose, Kubernetes workloads and cluster operations, health probes, graceful shutdown, CI/CD for images | Write a Dockerfile, a Compose file, a Kubernetes manifest, or a container pipeline stage |
| **ai/IAC.md** | Terraform and the AWS/Azure/Google Cloud/OCI templates in `infrastructure/`, provisioning of the platform and its backing services, state, policy-as-code | Provision or modify cloud infrastructure |
| **ai/API_CONTRACTS.md** | The API as the consumer-experience boundary: resource design, JSON conventions, Problem Details errors, pagination, localization, destructive-action safeguards, OpenAPI documentation and evolution | Design or change an endpoint, an error response, or a contract |

Tool-specific entry points derive from this file: `.claude/CLAUDE.md` imports it and adds Claude's working protocol; `.github/copilot-instructions.md` summarizes the key rules for GitHub Copilot (which cannot import files). Update the rules here first.

Every document ends with a **Definition of Done** for its domain and allows **documented exceptions only** (owner, scope, risk, rationale, expiration date). Silent deviation is never acceptable — if you cannot comply, say so and document why, rather than quietly shipping something out of spec.

Most tasks touch more than one document. A new endpoint that writes to the database, for example, is governed by ai/ARCHITECTURE.md (contract design), ai/DATABASE.md (persistence), ai/SECURITY.md (validation, authorization), ai/BUSINESS.md (the rule itself), and ai/API_CONTRACTS.md (the contract). Check the task-based index in Section 3 before starting.

---

## 2. Rules that apply regardless of task

These rules were reinforced, cross-referenced, or made mandatory across multiple documents. Violating any of them is a defect, not a style preference.

### Consistency and correctness

- **Idempotency is not optional.** Command handlers, RabbitMQ consumers, scheduled jobs, and any retried or destructive HTTP request must be safe to retry without duplicating the side effect. Use an `Idempotency-Key` (HTTP) or the event ID (messages) with a deduplication table (`ai/DATABASE.md §3.1`). See `ai/ARCHITECTURE.md §7.1/§8`, `ai/BUSINESS.md §6.2/§10.1`, `ai/CONTAINERS.md §6.7`, `ai/API_CONTRACTS.md §11.2`.
- **One aggregate, one transaction.** A single database transaction creates or modifies exactly one aggregate instance. Cross-aggregate changes go through domain/integration events and eventual consistency (`ai/ARCHITECTURE.md §4.1`).
- **Aggregate roots carry a `version` column and use optimistic concurrency on every update** (`ai/DATABASE.md §2.2`, `ai/ARCHITECTURE.md §4.1`).
- **Every persisted domain entity carries `id`, `created_at`, `updated_at`, `deleted_at`** (UTC), with soft-delete filtering enforced by EF Core global query filters, not by callers (`ai/DATABASE.md §2`).
- **Each bounded context owns its PostgreSQL schema and its own `DbContext`.** No cross-schema queries or foreign keys, no cross-context references to internal types (`ai/DATABASE.md §5`, `ai/ARCHITECTURE.md §2.3.3`).
- **Domain events carry a metadata envelope**: event ID, aggregate ID and version, UTC timestamp; integration events additionally carry correlation and causation IDs (`ai/ARCHITECTURE.md §4.4`, `ai/BUSINESS.md §4.2`).
- **State change + event publish must be atomic.** Write the event to the outbox table in the same transaction as the state change and publish to RabbitMQ through a separate relay. Never publish to the broker inside the write transaction (`ai/DATABASE.md §3.1`).
- **Money is `decimal` / `NUMERIC`, never `float`/`double`**, in code, in the database, and (as a decimal string plus ISO currency) in JSON (`ai/BUSINESS.md §7.1`, `ai/DATABASE.md §3.1`, `ai/API_CONTRACTS.md §3`).
- **All timestamps are UTC, and time comes from an injected `TimeProvider`.** A calendar date is a `DateOnly`/`date`, never a timestamp (`ai/BUSINESS.md §9.1`, `ai/DATABASE.md §3.1`).
- **Totals, prices, and discounts are always computed server-side** from authoritative data; client-supplied amounts are never trusted (`ai/SECURITY.md §2.3`).

### Security

- **Never log secrets, tokens, full session IDs, passwords, card data, or unredacted PII**, at any log level and in any telemetry signal (`ai/SECURITY.md §11.2`, `ai/OBSERVABILITY.md §14`).
- **Parameterized queries only. Never concatenate input into a query, command, or shell string** (no `FromSqlRaw` with concatenation).
- **Authorize every request, including object ownership.** Endpoints default to authenticated; a customer can only reach their own resources, and this is tested per endpoint (`ai/SECURITY.md §2.2`, `ai/BUSINESS.md §8`).
- **Least privilege everywhere**: PostgreSQL roles (runtime vs. migrator), RabbitMQ and Valkey users, cloud IAM, Kubernetes RBAC. No wildcard permissions without a documented exception.
- **Default-deny is the baseline**: Kubernetes network policies and security groups, ingress and egress (`ai/CONTAINERS.md §7.3`, `ai/IAC.md §7.3`); an unmapped role/action/resource combination is denied (`ai/BUSINESS.md §8.1`).
- **No secrets in code, `appsettings*.json`, images, Terraform state, or Git history**, including `.tfvars` and `.env` files. Secrets are injected at runtime from a secrets manager (`ai/SECURITY.md §5`, `ai/IAC.md §6`, `ai/CONTAINERS.md §2.4`).
- **Never store card numbers or CVV.** Payment data is tokenized by the provider; payment webhooks are signature-verified and deduplicated (`ai/DATABASE.md §9`, `ai/SECURITY.md §2.3`).
- **Don't invent cryptography.** Use the algorithms in `ai/SECURITY.md §4` (ChaCha20-Poly1305, Argon2id — not the default ASP.NET Core Identity hasher — and SHA3-512) through the libraries in `§4.4`, and `RandomNumberGenerator` for anything security-relevant.
- **Containers run as non-root, read-only root filesystem, all capabilities dropped, no privilege escalation** — in Compose and in Kubernetes (`ai/CONTAINERS.md §3.1`).
- **Dependencies come from the recommended libraries** (start at `ai/CODE.md §5`), are license-approved, and are restored from committed lock files in locked mode. Several popular packages are now commercial (MediatR 13+, AutoMapper 15+, MassTransit 9+, FluentAssertions 8+, Duende IdentityServer); do not add or upgrade into them without approval (`ai/CODE.md §4.3`, `ai/SECURITY.md §9`).
- **Errors fail closed** (OWASP A10:2025): an exception in an auth, validation, or payment path denies; clients get RFC 9457 Problem Details without internals (`ai/SECURITY.md §2`, `ai/BUSINESS.md §5.3`, `ai/API_CONTRACTS.md §4`).
- **Swagger/OpenAPI and Scalar are development-only.** Health and metrics endpoints are never routed publicly (`ai/SECURITY.md §6.6`).

### API contract work

- **The API contract never leaks domain, persistence, or infrastructure details**, and is versioned (`/api/v1/...`), backward-compatible within a version, and fully described in OpenAPI (`ai/ARCHITECTURE.md §7.1`, `ai/API_CONTRACTS.md §2, §12`).
- **Errors are RFC 9457 Problem Details** with stable machine-readable `code`s, `ruleId`, field pointers, and plain-language messages (`ai/API_CONTRACTS.md §4`).
- **Every collection is paginated**; an empty result is `200` with `[]`, never `404` (`ai/API_CONTRACTS.md §5–§6`).
- **Destructive and high-impact actions are protected by the API itself**: `Idempotency-Key`, preconditions (`If-Match`), server-enforced confirmation, step-up authentication where required, and audit logging (`ai/API_CONTRACTS.md §11`).
- **User-facing text is localizable**: stable error codes plus resource-file messages, no hardcoded strings in controllers or the domain (`ai/API_CONTRACTS.md §7`).

### Testing

- **Unit tests touch no infrastructure.** No database, network, file system, or real clock — inject fakes and `FakeTimeProvider` (`ai/TESTS.md §3.1`).
- **Integration tests use real PostgreSQL, Valkey, and RabbitMQ in Testcontainers** — never the EF Core in-memory or SQLite providers (`ai/TESTS.md §4.1`).
- **Follow the pyramid**: mostly unit tests (70–80%), some integration tests (15–25%), few E2E tests (5–10%) covering only critical flows (`ai/TESTS.md §2.2`).
- **Architecture tests enforce the layer and context boundaries** and run in CI (`ai/ARCHITECTURE.md §10`, `ai/TESTS.md §9.4`).
- **A flaky test is quarantined within 24 hours and fixed or removed within 2 sprints** — it is not silently re-run until green (`ai/TESTS.md §11`).
- **Idempotency of handlers and consumers is a test requirement**: processing the same command or message twice produces no duplicate side effect (`ai/TESTS.md §4.4`).
- Test names describe domain behavior (`Should_reject_order_when_total_is_below_minimum`), not implementation.

### Everything ships with

- Tests appropriate to the layer, per `ai/TESTS.md`.
- Structured logs, metrics, and traces sufficient for production diagnosis via OpenTelemetry — no telemetry code in the Domain layer (`ai/OBSERVABILITY.md §3–§6`, `ai/SECURITY.md §11`).
- A rollback path, defined before the change ships, for anything touching production data or infrastructure (expand/contract migrations, `ai/DATABASE.md §6`).

---

## 3. Task-based index

| You are about to... | Go to |
| --- | --- |
| Design a new bounded context or module | `ai/ARCHITECTURE.md §2`–`§6` |
| Write a domain entity, aggregate, or value object | `ai/ARCHITECTURE.md §4`, `ai/BUSINESS.md §4` |
| Add or change a business rule | `ai/BUSINESS.md §2`–`§3` (classify it, document it with a `BR-` ID) |
| Build a state machine / workflow / saga | `ai/BUSINESS.md §6` |
| Write a calculation involving money, prices, or promotions | `ai/BUSINESS.md §7` |
| Add an authorization/access rule | `ai/BUSINESS.md §8` (business-level), `ai/SECURITY.md §2.2`, `§7`–`§8` (technical enforcement) |
| Design or change an endpoint / API contract | `ai/ARCHITECTURE.md §7`, `ai/API_CONTRACTS.md §2`–`§6`, `§12`, `ai/SECURITY.md §2.2` |
| Design or change an error response or validation | `ai/BUSINESS.md §5`, `ai/API_CONTRACTS.md §4` |
| Add a destructive or high-impact action (delete, refund, cancel, charge) | `ai/API_CONTRACTS.md §11`, `ai/SECURITY.md §2.3` |
| Write C# / .NET code | `ai/CODE.md §4`, toolchain versions in `§2.3` |
| Choose a library or add a dependency | `ai/CODE.md §5`, plus `ai/ARCHITECTURE.md §9.4`, `ai/DATABASE.md §7.4`, `ai/SECURITY.md §4.4`/`§14`, `ai/BUSINESS.md §14`, `ai/API_CONTRACTS.md §13`, `ai/CONTAINERS.md §13`, `ai/OBSERVABILITY.md §16` |
| Open a pull request | `ai/CODE.md §6` |
| Write a unit test | `ai/TESTS.md §3`, `§9.2` |
| Write an integration, contract, or architecture test | `ai/TESTS.md §4`–`§5`, `§9.3`–`§9.4` |
| Write an E2E or performance test | `ai/TESTS.md §6`–`§7` |
| A test is failing intermittently | `ai/TESTS.md §11` (quarantine within 24h, don't just re-run it) |
| Add or change a database table/column | `ai/DATABASE.md §2` (traceability fields, mandatory), `§3`, `§5` (schema ownership) |
| Write a migration | `ai/DATABASE.md §6` |
| Write or review EF Core / Npgsql code | `ai/DATABASE.md §7` |
| Publish or consume a RabbitMQ event | `ai/ARCHITECTURE.md §9.3`, `ai/DATABASE.md §3.1`, `ai/TESTS.md §4.4`, `ai/OBSERVABILITY.md §6.4` |
| Cache something in Valkey | `ai/DATABASE.md §4` |
| Touch authentication, JWT, or OAuth2 | `ai/SECURITY.md §7`–`§8` |
| Handle a password | `ai/SECURITY.md §4.2` |
| Handle a secret, key, or connection string | `ai/SECURITY.md §5` |
| Integrate a payment provider, webhook, or other third party | `ai/SECURITY.md §2.3`, `ai/BUSINESS.md §10`, `ai/ARCHITECTURE.md §9.1` |
| Accept file uploads | `ai/SECURITY.md §6.5` |
| Write a Dockerfile | `ai/CONTAINERS.md §2`, `§13.1` |
| Write or change a Docker Compose file | `ai/CONTAINERS.md §5` |
| Write a Kubernetes manifest | `ai/CONTAINERS.md §6`–`§7` |
| Add a health probe or handle shutdown | `ai/CONTAINERS.md §8` |
| Add logs, metrics, or traces to a code path | `ai/OBSERVABILITY.md §3`–`§6`, `§16` |
| Define an SLO, an alert, or a dashboard | `ai/OBSERVABILITY.md §9`–`§11` |
| Change the Collector or the observability stack | `ai/OBSERVABILITY.md §12`–`§13` |
| Provision cloud infrastructure | `ai/IAC.md §3`–`§5` (provider templates: `§5.1`, usage: `§5.2`) |
| Handle a secret in infrastructure code | `ai/IAC.md §6`, `ai/SECURITY.md §5` |
| Write user-facing copy (error messages, notification text) | `ai/API_CONTRACTS.md §7`, `§10` |

---

## 4. Before you consider a task done

Every document has its own Definition of Done; this is only a first-pass filter. If any item does not apply to your change, say so rather than skipping silently:

1. The change is expressed in domain language and traceable to a documented business rule, requirement, or ADR.
2. New/changed database tables have traceability fields; aggregate roots have a `version` column; the change stays inside its context's schema and ships as a reviewed EF Core migration.
3. Anything that writes state and publishes an event does both atomically (outbox).
4. Anything retriable (command, consumer, job, destructive HTTP request) is idempotent.
5. No secret, token, or PII appears in code, configuration, logs, images, or infrastructure state.
6. Least-privilege access is applied to every new role, credential, or network path; unmapped access is denied by default; object ownership is enforced and tested.
7. New/changed endpoints are versioned, documented in OpenAPI, return Problem Details errors, paginate collections, and — if destructive — carry the API-level safeguards of `ai/API_CONTRACTS.md §11`.
8. Tests cover the change at the appropriate layer(s) per `ai/TESTS.md` (unit tests need no infrastructure; the pyramid is respected) and pass in CI; quality gates from `ai/CODE.md §2` are green, including architecture tests.
9. Logs/metrics/traces exist for the new code path per `ai/OBSERVABILITY.md`, and SLOs, alerts, and dashboards are updated when a critical journey changes.
10. A rollback plan exists for anything touching production.
11. Any deviation from the above is documented with owner, scope, risk, rationale, and expiration date — not left unstated.

---

## 5. How the documents relate to each other

Cross-references between these documents are intentional — a change to one may require a corresponding update elsewhere:

- **ai/SECURITY.md is foundational.** The other documents defer to it for cryptography, secrets, logging redaction, and access-control enforcement.
- **ai/ARCHITECTURE.md and ai/BUSINESS.md co-define the domain model.** ai/ARCHITECTURE.md owns the structural rules (contexts, aggregates, layering, events-as-artifacts); ai/BUSINESS.md owns the rule content (classification, calculations, authorization, temporal logic).
- **ai/DATABASE.md implements what ai/ARCHITECTURE.md and ai/BUSINESS.md require**: the `version` column exists because of optimistic concurrency; the outbox and inbox tables exist because domain events must be reliable; schema-per-context exists because contexts own their data.
- **ai/CONTAINERS.md and ai/IAC.md form the infrastructure layer**: what runs (images, Compose, Kubernetes) versus what provisions the platform (Terraform), each deferring to ai/SECURITY.md.
- **ai/CODE.md is the language-level baseline** that ai/ARCHITECTURE.md's SOLID/testability principles and ai/DATABASE.md's EF Core practices build on.
- **ai/TESTS.md cross-cuts every other document.** It extends ai/BUSINESS.md §12 (domain rule tests), ai/DATABASE.md §8 (database tests), ai/API_CONTRACTS.md §12 (contract tests), and ai/SECURITY.md (security tests) with the mechanics of how to write them.
- **ai/OBSERVABILITY.md cross-cuts the runtime.** It implements the telemetry that ai/ARCHITECTURE.md §8, ai/CODE.md §1, and ai/CONTAINERS.md §3.5/§11 require, defers to ai/SECURITY.md §11 for what may be logged, is deployed under ai/IAC.md and ai/CONTAINERS.md, and defines the SLO thresholds that ai/TESTS.md §7 validates.
- **ai/API_CONTRACTS.md is the outermost layer** — the API contract seen by clients — deferring to ai/BUSINESS.md for domain language and authorization, ai/SECURITY.md for data exposure and session handling, and ai/ARCHITECTURE.md §7 for contract design rules.

If a task requires bending a rule in one document to satisfy another, resolve the tension explicitly — as a documented exception, or as an ADR under `ai/ARCHITECTURE.md §11` — not by quietly picking one side.

---

_Last updated: September 30, 2026_
_These standards must be reviewed and updated at minimum once per year or after any significant agent incident._

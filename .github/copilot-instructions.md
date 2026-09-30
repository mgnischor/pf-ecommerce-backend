# GitHub Copilot Instructions

This is a C# / .NET 10 **modular monolith** (DDD by bounded context under `src/`) using PostgreSQL (EF Core + Npgsql), Valkey, RabbitMQ, and OpenTelemetry. It is an HTTP API; there is no UI. Follow the standards in the `ai/` folder — they are non-negotiable. Do not suggest code, architecture, or guidance that contradicts them.

The canonical rules and the task-to-document index are in [`AGENTS.md`](../AGENTS.md). Copilot cannot import files, so the most important rules are summarized below; if you change one, change `AGENTS.md` too.

## Rules

- **Architecture:** Dependencies flow `API → Application → Domain ← Infrastructure`. Domain has no EF Core, ASP.NET Core, RabbitMQ, Valkey, or telemetry dependencies. Contexts never reference each other's internals and never share tables: one schema and one `DbContext` per context.
- **Business rules:** Invariants live inside aggregates and entities, each traceable to a `BR-` rule and testable in isolation. One aggregate per transaction.
- **Database:** Every entity has `id`, `created_at`, `updated_at`, `deleted_at` (nullable timestamp, never `IsDeleted`); aggregate roots add a `version` concurrency token. Schema changes are reviewed EF Core migrations (expand/contract). Parameterized SQL only. Events are published through the transactional outbox; handlers and consumers are idempotent.
- **Money and time:** `decimal`/`NUMERIC` for money (decimal string plus ISO currency in JSON); UTC everywhere; inject `TimeProvider`. Totals are computed server-side.
- **API:** Versioned (`/api/v1/`), documented in OpenAPI, RFC 9457 Problem Details with stable error codes, paginated collections, no domain or entity leakage, `Idempotency-Key` on retried or destructive requests.
- **Security:** OWASP Top 10:2025 and API Top 10. Authenticated by default; check object ownership on every resource. ChaCha20-Poly1305, Argon2id, SHA3-512 via approved libraries only. No secrets in code, config, images, or logs; no PII or card data in logs or telemetry.
- **Code:** SOLID, nullable enabled, `internal` by default, analyzers as errors, recommended libraries only (`ai/CODE.md §5`). No MediatR 13+, AutoMapper 15+, MassTransit 9+, or FluentAssertions 8+ without approval.
- **Tests:** Unit tests use no infrastructure; integration tests use Testcontainers (real PostgreSQL, Valkey, RabbitMQ). Names describe domain behavior. Coverage ≥ 80% line, ≥ 70% branch; quarantine flaky tests.
- **Containers and IaC:** Chiseled, digest-pinned images; non-root, read-only root filesystem, dropped capabilities, resource limits; default-deny network policies; secrets from a secrets manager. Infrastructure is declarative (Terraform with AWS, Azure, Google Cloud, and OCI templates in `infrastructure/`, Kustomize) with pinned versions.
- **Observability:** OpenTelemetry only, never in the Domain layer; OTLP to the Collector; W3C trace context across HTTP, RabbitMQ, and the outbox; bounded-cardinality metrics.

## Behavior

- Read the relevant `ai/*.md` document(s) before generating output in a domain, and apply all that are relevant.
- If a request conflicts with a standard, follow the standard and explain the conflict. Exceptions need owner, scope, risk, rationale, and expiration date.
- Never disable security controls or bypass validation. Prefer small, reversible changes.
- Track work in [`ai/TASKS.md`](../ai/TASKS.md): mark `[~]` when starting and `[x]` when done; never edit task descriptions.

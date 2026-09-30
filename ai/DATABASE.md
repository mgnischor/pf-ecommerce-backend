# Database Standards

> **Scope:** These standards apply to all persistence in the `pf-ecommerce-backend` repository: **PostgreSQL** accessed through **Entity Framework Core** and **Npgsql**, and **Valkey** used as a cache. Every item marked as mandatory is a hard requirement unless a documented exception is approved by the maintainers. Database operations prioritize consistency, performance, security, traceability, and evolvability.

---

## Table of Contents

1. [General Principles](#1-general-principles)
2. [Mandatory Entity Traceability](#2-mandatory-entity-traceability)
3. [PostgreSQL Standards](#3-postgresql-standards)
    - 3.1 [Universal SQL Rules](#31-universal-sql-rules)
    - 3.2 [PostgreSQL Rules](#32-postgresql-rules)
    - 3.3 [E-Commerce Data Patterns](#33-e-commerce-data-patterns)
4. [Valkey and Caching](#4-valkey-and-caching)
    - 4.1 [Valkey](#41-valkey)
    - 4.2 [General Caching Rules](#42-general-caching-rules)
5. [Schema Ownership per Bounded Context](#5-schema-ownership-per-bounded-context)
6. [Migrations and Schema Versioning](#6-migrations-and-schema-versioning)
7. [EF Core and Data Access Practices](#7-ef-core-and-data-access-practices)
    - 7.1 [Entity Framework Core](#71-entity-framework-core)
    - 7.2 [Npgsql](#72-npgsql)
    - 7.3 [Dapper (Exception Path)](#73-dapper-exception-path)
    - 7.4 [Recommended Data Access Libraries](#74-recommended-data-access-libraries)
8. [Database Testing Strategy](#8-database-testing-strategy)
9. [Data Governance and Compliance](#9-data-governance-and-compliance)
10. [Operations, Observability, and Resilience](#10-operations-observability-and-resilience)
11. [Database Definition of Done](#11-database-definition-of-done)

---

<GeneralPrinciples>
## 1. General Principles

| Principle | Mandatory Behavior |
| --- | --- |
| **Reliable Source of Truth** | PostgreSQL represents the official business state with integrity and consistency. Valkey never does. |
| **Schema as Code** | Every structural change is versioned through EF Core migrations in the repository. |
| **Complete Traceability** | Every persisted entity records creation, update, and logical deletion lifecycle data. |
| **Security by Default** | Least privilege, encryption in transit and at rest, no sensitive data in logs. |
| **Evidence-Based Performance** | Indexes, queries, and tuning are driven by measurements (execution plans, metrics). |
| **Safe Evolution** | Schema changes are backward-compatible during transition windows (expand/contract). |
| **Secure and Deterministic Failure** | Persistence failures are handled with rollback and controlled error messaging. |

</GeneralPrinciples>

---

<Traceability>
## 2. Mandatory Entity Traceability

Every persisted entity includes identity and traceability fields.

### 2.1 Mandatory fields

| Column | C# property | Type | Notes |
| --- | --- | --- | --- |
| `id` | `Id` | `uuid` | Generated in the domain (`Guid.CreateVersion7()` — time-ordered, index-friendly) or as a typed ID wrapping a `Guid`. |
| `created_at` | `CreatedAt` | `timestamptz` | UTC. |
| `updated_at` | `UpdatedAt` | `timestamptz` | UTC. |
| `deleted_at` | `DeletedAt` | `timestamptz` null | UTC; `NULL` while active. A boolean `is_deleted` flag is **not** an acceptable substitute — it loses when the deletion happened and cannot support retention or restore windows. |

Column names are `snake_case` (via `EFCore.NamingConventions` `UseSnakeCaseNamingConvention()`); C# properties are `PascalCase` and mapped by EF Core. Both refer to the same mandatory fields.

### 2.2 Mandatory rules

- Every domain table has an explicit primary key (`id`); composite keys only when justified by the data model (e.g., pure join tables).
- Traceability fields exist in all domain tables, except explicitly documented technical tables (outbox, inbox/idempotency, EF migration history).
- Physical deletion is avoided for business entities; use logical deletion with `deleted_at`.
- `updated_at` is set automatically on every change by a `SaveChanges` interceptor using the injected `TimeProvider`.
- Business queries exclude records where `deleted_at` is set by default, except in admin/audit flows.
- Soft-delete filtering is enforced at the persistence layer with EF Core global query filters (named filters in EF Core 10), so developers never filter manually. Use `IgnoreQueryFilters()` (or the specific named filter) only in audited admin/audit flows.
- The table backing an **aggregate root** has a `version` column (`integer`, incremented by the aggregate on every state change) used as an optimistic-concurrency token on every update, per [ARCHITECTURE.md](./ARCHITECTURE.md) Section 4.1. This is mandatory for aggregate roots specifically.

### 2.3 Recommended

- Also include `created_by`, `updated_by`, and `deleted_by` when the actor identity is available (populated by the same interceptor from an `ICurrentActor` abstraction).
- For non-aggregate-root entities, include a `version` column when the access pattern shows a realistic risk of concurrent updates.

</Traceability>

---

<PostgreSQLStandards>
## 3. PostgreSQL Standards

PostgreSQL is the only system of record. Run a vendor-supported major version (16 minimum; newest supported preferred).

### 3.1 Universal SQL Rules

| Topic | Mandatory |
| --- | --- |
| **Secure Queries** | Always parameterized. Never concatenate user input into SQL. With EF Core, `FromSql` (interpolated) and `ExecuteSql` only — `FromSqlRaw`/`ExecuteSqlRaw` with concatenated strings are forbidden. |
| **Transactions** | Explicit transactions for multi-step operations, with rollback on failure. One transaction per use case; one aggregate per transaction ([AGENTS.md](../AGENTS.md)). |
| **Referential Integrity** | Primary keys, foreign keys (within a schema), and domain constraints (`NOT NULL`, `CHECK`, `UNIQUE`). |
| **Indexing** | Indexes based on real query patterns; review selectivity. Foreign-key columns used in joins are indexed. |
| **Query Discipline** | No `SELECT *` (project only required columns). Deterministic ordering for paginated queries; keyset pagination for large sets. |
| **N+1 and Overfetching** | Never issue one query per row of a previous result; use projections, joins, or `AsSplitQuery`, and assert query counts in tests for critical paths. |
| **Normalization** | Normalize by default; denormalize (read models, projections) only with a performance justification and a consistency plan. |
| **Timezone** | Store timestamps as `timestamptz` in UTC; convert only at presentation. |
| **Monetary Precision** | Money columns use `numeric(19,4)` (or a scale justified in an ADR) plus a `currency` column (`char(3)`, ISO 4217). `real`/`double precision` are forbidden for money ([BUSINESS.md](./BUSINESS.md) Section 7.1). |

#### Additional mandatory rules

- Define command and connection timeouts in the application.
- Avoid business logic in stored procedures, functions, and triggers. Business rules live in the domain; triggers are reserved for technical concerns and are versioned and tested.
- Version scripts for indexes, constraints, and auxiliary objects (all through migrations).
- **Transactional outbox.** When an operation must both change state and publish an event ([ARCHITECTURE.md](./ARCHITECTURE.md) Sections 4.4 and 9.3), write the event to an `outbox_messages` table **in the same transaction** as the state change (via a `SaveChanges` interceptor that converts raised domain events into rows). A separate relay publishes to RabbitMQ and marks rows as processed. The outbox table carries: `id` (event ID), `type`, `payload` (`jsonb`), `occurred_at`, `correlation_id`, `causation_id`, `processed_at`, `attempts`. Never publish to RabbitMQ inside the same transaction as the state change — the broker is not transactional with PostgreSQL. The relay claims rows with `SELECT ... FOR UPDATE SKIP LOCKED` so multiple instances can run safely.
- **Idempotency / inbox.** To deduplicate retried commands or at-least-once RabbitMQ deliveries, persist processed request/event IDs in a dedicated table (`inbox_messages` / `idempotency_keys`, `UNIQUE` on the key, plus a stored response for HTTP idempotency keys) and check it in the same transaction as the side effect, per [ARCHITECTURE.md](./ARCHITECTURE.md) Section 8. Expired keys are purged by a scheduled job.

### 3.2 PostgreSQL Rules

- Use native types: `uuid`, `jsonb`, `timestamptz`, `numeric`, `text` (with `CHECK` length constraints where a limit matters), `date` for calendar dates, PostgreSQL `enum` only for truly stable sets (prefer `text` + `CHECK` or a lookup table, which migrate more easily).
- Enforce data quality at the database layer with `NOT NULL`, `CHECK`, `UNIQUE`, and `FOREIGN KEY`.
- For `jsonb`, create GIN indexes only when execution-plan evidence justifies them. Do not hide relational data in `jsonb`.
- Use partial and composite indexes that match real filter/sort patterns. Unique business keys that coexist with soft delete use **partial unique indexes** (`CREATE UNIQUE INDEX ... WHERE deleted_at IS NULL`).
- Validate critical queries with `EXPLAIN (ANALYZE, BUFFERS)` before production and after major data growth.
- Keep SQL set-based; avoid row-by-row processing in application loops for high-volume operations.
- Keep transactions short; choose isolation levels explicitly for consistency-critical workflows (default `READ COMMITTED`; use `SERIALIZABLE` with retry, or explicit row locks, only where the invariant requires it).
- Control locking behavior: avoid long-held locks in request flows; never hold a transaction open across an HTTP or broker call.
- Use dedicated least-privilege roles: `app_runtime` (DML on its schemas only, no DDL), `app_migrator` (DDL, used only by the migration job), `app_readonly` (reporting). Never use a superuser for the application.
- Enable TLS (`sslmode=verify-full`) for all non-local connections. Secrets and connection strings are never logged.
- Monitor slow queries (`pg_stat_statements`), lock contention, autovacuum health, bloat, connection count, replication lag, and disk growth.
- Treat `VACUUM`/`ANALYZE` health as a continuous operational responsibility; tune autovacuum per hot table when measured.
- Evaluate partitioning (e.g., outbox, audit, event tables by time) only when workload patterns clearly benefit from partition pruning and lifecycle management.
- Full-text search in Catalog uses PostgreSQL FTS (`tsvector` + GIN) and/or `pg_trgm`; a dedicated search engine requires an ADR.

### 3.3 E-Commerce Data Patterns

| Pattern | Mandatory Behavior |
| --- | --- |
| **Stock changes** | Decrement/reserve atomically in a single statement with a guard (`UPDATE inventory_items SET available = available - @qty, version = version + 1 WHERE id = @id AND available >= @qty`), treating zero rows as "insufficient stock". Never read-modify-write stock in application memory without a lock or concurrency token. A `CHECK (available >= 0)` constraint is the safety net. |
| **Reservations** | Stock reservations carry an `expires_at`; a scheduled job releases expired ones idempotently ([BUSINESS.md](./BUSINESS.md) Section 9.2). |
| **Price snapshot** | Order lines copy unit price, discounts, tax, and currency at purchase time; they never join to live catalog prices ([BUSINESS.md](./BUSINESS.md) Section 7.1). |
| **Human-readable numbers** | Order/invoice numbers come from a PostgreSQL sequence (or a dedicated generator), are unique, and are separate from the `uuid` primary key. |
| **Append-only history** | Payments, refunds, stock movements, and status transitions are recorded as append-only rows (ledger style); corrections are compensating rows, never updates. |
| **Coupon redemption** | Redemption counts are enforced with a `UNIQUE` constraint or guarded `UPDATE`, so concurrent redemptions cannot exceed the limit. |

</PostgreSQLStandards>

---

<ValkeyAndCaching>
## 4. Valkey and Caching

### 4.1 Valkey

Valkey (BSD-licensed, Redis-protocol compatible) is the project's cache. It is **not** the source of truth for durable business state. Carts, orders, and stock live in PostgreSQL; if a context proposes Valkey as a primary store, that requires an ADR with persistence (AOF) and recovery design.

| Topic | Mandatory |
| --- | --- |
| **Purpose-Driven Usage** | Caching, rate-limit counters, short-lived idempotency/lock hints, and pub/sub notifications — not durable business state. |
| **Data Expiration** | Explicit TTL on every key; never rely on unbounded key growth. |
| **Serialization** | Compact, versioned serialization (`System.Text.Json` with source generation, or MessagePack/Protobuf). Include a schema version in the key or payload. |
| **Security** | Per-service ACL users (not the `default` user), TLS for all non-local connections, and deny `KEYS`, `FLUSHALL`, `FLUSHDB`, `CONFIG`, and `DEBUG` via ACL in production. |
| **Key Naming** | Namespaced and versioned: `ecommerce:{context}:{entity}:{id}:v{schemaVersion}`, e.g., `ecommerce:catalog:product:0192f…:v2`. Never include PII in keys. |

Additional rules:

- Configure `maxmemory` explicitly and an eviction policy suited to the workload (`allkeys-lru` or `allkeys-lfu` for a pure cache; `noeviction` for an instance holding rate-limit or idempotency state that must not be silently dropped). Separate pure-cache and stateful-hint workloads into different instances/databases when their eviction needs differ.
- Use `SCAN` (never `KEYS`) for key iteration.
- Use pipelining/batching to reduce round trips; use Lua scripts or `MULTI`/`EXEC` for atomic multi-step operations.
- Distributed locks (`SET key token NX PX ttl` with compare-and-delete release) are for **efficiency** only (avoiding duplicate work); they do not guarantee mutual exclusion under process pauses or failover. When correctness depends on mutual exclusion, use a PostgreSQL row lock, advisory lock, or unique constraint.
- Do not store PII, credentials, or tokens in Valkey without encryption and a short TTL. Never cache authorization decisions across users.
- Persistence (`RDB`/`AOF`) is disabled for pure-cache instances and enabled only when durability of the stored hints is required.
- For Valkey Cluster/Sentinel or managed offerings, validate failover behavior and client support before go-live.
- Monitor memory, fragmentation, connected clients, eviction rate, hit/miss ratio, keyspace size, and replication lag.

### 4.2 General Caching Rules

| Topic | Mandatory |
| --- | --- |
| **Cache-Aside by Default** | The application reads through the cache (`HybridCache`/`IDistributedCache`) and falls back to PostgreSQL on a miss. Caching lives in Infrastructure ([ARCHITECTURE.md](./ARCHITECTURE.md) Section 8). |
| **Invalidation Strategy** | Explicit: TTL plus event-driven invalidation (a domain/integration event evicts the affected keys). Document it per cached item. |
| **Stampede Prevention** | `HybridCache` stampede protection, per-key locking, or stale-while-revalidate for hot keys. |
| **Serialization Versioning** | Backward-compatible payloads; bump the key version when the cached schema changes. |
| **Fallback Behavior** | The application works correctly (slower) when Valkey is unavailable. Cache failures are logged and swallowed at the cache boundary, never surfaced as request failures. |
| **Monitoring** | Hit/miss ratio, latency, eviction rate, and memory are mandatory metrics. |
| **Data Sensitivity** | No sensitive data cached without encryption and a controlled TTL. |

Additional rules:

- Document which data is cached, where, with which TTL, and with which invalidation trigger (e.g., product details: 5 min TTL, evicted on `ProductUpdated`).
- Avoid caching mutable, consistency-critical data (stock availability at checkout, prices at order placement) — read those from PostgreSQL inside the transaction.
- Multi-layer caching (in-memory L1 + Valkey L2) via `HybridCache` only when latency or load justifies it.
- Test cache behavior under cold start, high concurrency, and Valkey outage.

</ValkeyAndCaching>

---

<SchemaOwnership>
## 5. Schema Ownership per Bounded Context

The repository is a modular monolith on **one PostgreSQL database** ([ARCHITECTURE.md](./ARCHITECTURE.md) Section 9.2). Data ownership is enforced as follows:

| Rule | Mandatory Behavior |
| --- | --- |
| **One schema per context** | Each bounded context owns a PostgreSQL schema named after it in `snake_case` (`catalog`, `cart`, `checkout`, `ordering`, `billing`, `inventory`, `shipping`, `promotions`, `reviews`, `customers`, `identity`, `notifications`). Shared technical tables (none by default) require an ADR. |
| **One `DbContext` per context** | Each context has its own `DbContext` (`OrderingDbContext`, …) with `HasDefaultSchema("ordering")`. A `DbContext` never maps another context's entities. |
| **Own migration history** | Each `DbContext` has its own migration folder and history table in its own schema (`MigrationsHistoryTable("__ef_migrations_history", "ordering")`). |
| **No cross-schema foreign keys** | No foreign keys, joins, views, or queries across context schemas. Cross-context references are stored as plain IDs and resolved through public context interfaces or integration events. |
| **Per-context privileges** | Where practical, the runtime role has privileges only on the schemas it needs; at minimum, grants are explicit per schema. |
| **Read models** | A context that needs another context's data keeps its own local projection, fed by integration events — not a cross-schema query. |
| **Outbox and inbox per context** | Each context owns its `outbox_messages` and `inbox_messages` tables in its schema, so its transaction remains self-contained. |
| **Architecture test** | A test asserts that a `DbContext` maps only entities of its own context and that no SQL/config references another context's schema. |

</SchemaOwnership>

---

<Migrations>
## 6. Migrations and Schema Versioning

Database changes are predictable, auditable, and reproducible.

### 6.1 Mandatory requirements

- Every schema change is an **EF Core migration** versioned in the repository, in the owning context's `Infrastructure/Persistence/Migrations` folder, created with an explicit `--context`.
- Migration names are descriptive and follow `<Action><Entity><Detail>` in PascalCase; EF Core adds the timestamp prefix: `20260430103000_AddIndexOrdersCreatedAt`, `…_CreateTableCustomerPreferences`, `…_AlterColumnInvoiceTotalPrecision`.
- Review the generated migration and the SQL it produces (`dotnet ef migrations script --idempotent`) in the pull request. Generated migrations are never merged unread.
- Migrations are applied in deployment by a **migrations bundle** (`dotnet ef migrations bundle`) run as a pipeline/Kubernetes Job with the `app_migrator` role ([CONTAINERS.md Section 6.7](./CONTAINERS.md)). Automatic `Database.Migrate()` at application startup is forbidden outside local development.
- Hand-written SQL in a migration (`migrationBuilder.Sql`) is idempotent where possible and reviewed like any code.
- Destructive migrations (drop/rename without compatibility) require a documented rollout and rollback plan.
- Every migration has a tested `Down` or a documented forward-fix plan; data-destroying `Down` methods are explicit.

### 6.2 Rollout rules (expand/contract)

- Split risky changes into phases: **expand** (add nullable column/new table), **migrate** (backfill, dual-write), **contract** (drop the old structure in a later release).
- Never ship an incompatible schema change in the same release as the application code that depends on it. Rolling deployments run old and new versions against the same schema ([CONTAINERS.md Section 6.2](./CONTAINERS.md)).
- On large tables, create indexes with `CREATE INDEX CONCURRENTLY` (`migrationBuilder.Sql(..., suppressTransaction: true)`), add `NOT NULL`/foreign keys in two steps (`NOT VALID` then `VALIDATE CONSTRAINT`), and set `lock_timeout` for DDL to avoid blocking production traffic.
- Validate migrations in an environment close to production with representative data volumes.
- Backfills track progress and support safe resumption after failures.

</Migrations>

---

<DataAccessPractices>
## 7. EF Core and Data Access Practices

### 7.1 Entity Framework Core

- Use EF Core migrations for schema versioning ([Section 6](#6-migrations-and-schema-versioning)).
- Configure mappings only with `IEntityTypeConfiguration<T>` classes in `Infrastructure/Persistence/Configurations` — no data annotations on domain types ([ARCHITECTURE.md](./ARCHITECTURE.md) Section 3.3). Configure indexes, constraints, column types, and relationships explicitly (fluent API).
- Map value objects as owned types or value converters; map strongly typed IDs with value converters; map private collections with field access.
- Domain entities have no EF navigation properties to other aggregates — reference other aggregates by ID only.
- Use `AsNoTracking()` (or projections with `Select`) for read-only paths; avoid unnecessary `Include` graphs; use `AsSplitQuery()` for multi-collection includes.
- Ensure automatic population of `created_at`, `updated_at`, and `deleted_at` (soft delete) by a `SaveChanges` interceptor using `TimeProvider`; deleting an aggregate through a repository sets `DeletedAt` instead of issuing `DELETE`.
- Use global query filters for soft delete (`deleted_at IS NULL`) and, where suitable, ownership filtering. Named filters (EF Core 10) allow disabling one without the other.
- Use `ExecuteUpdateAsync`/`ExecuteDeleteAsync` for bulk operations instead of loading entities. They bypass the change tracker, interceptors, and concurrency tokens: set `updated_at` (and `deleted_at`/`version`) explicitly in the statement, and never use them on aggregate roots where optimistic concurrency is required.
- Map the aggregate root's `version` as a concurrency token (`IsConcurrencyToken()`; or `IsRowVersion()` on a `xmin`-mapped `uint` when an additional database-maintained token is wanted) and translate `DbUpdateConcurrencyException` into a domain-level conflict (`409 Conflict`, [BUSINESS.md](./BUSINESS.md) Section 5.3).
- Register `DbContext`s with `AddDbContextPool` where they hold no per-request state; use a single shared `NpgsqlDataSource`.
- Use EF Core's retrying execution strategy (`EnableRetryOnFailure`) for transient failures; when using explicit transactions, wrap the unit of work in `CreateExecutionStrategy().ExecuteAsync(...)` so the whole transaction is retried, and keep it idempotent.
- Explicitly control transactions in use cases that span multiple repositories; a use case ends with exactly one `SaveChangesAsync` per aggregate transaction.
- Review EF-generated SQL for critical paths (`LogTo`/`ToQueryString()` in development; `EXPLAIN` in staging). Enable `EnableSensitiveDataLogging` only in local development.
- No lazy loading proxies. Compiled queries or compiled models only when measured.

### 7.2 Npgsql

- One `NpgsqlDataSource` per database (registered as a singleton), configured with explicit `MaxPoolSize`, `CommandTimeout`, `Timeout`, and TLS (`SslMode=VerifyFull` outside local). `replicas × MaxPoolSize` must stay below the server's `max_connections` budget (consider PgBouncer in transaction mode if not).
- Enable Npgsql's OpenTelemetry instrumentation (`Npgsql.OpenTelemetry`) for traces and metrics ([OBSERVABILITY.md](./OBSERVABILITY.md)).
- Use `NpgsqlBatch`/EF Core batching for multi-statement work; use binary `COPY` for large bulk imports.
- Use `jsonb` mapping via `System.Text.Json`; avoid dynamic JSON types in domain models.

### 7.3 Dapper (Exception Path)

- Permitted only in Infrastructure for **read-side** hot paths where EF Core overhead has been measured, with parameterized SQL only and an ADR. Dapper must not write aggregate state.

### 7.4 Recommended Data Access Libraries

Use the defaults below unless an ADR records a different choice; unmaintained drivers are forbidden.

| Need | Default |
| --- | --- |
| **ORM / query layer** | Entity Framework Core (`Microsoft.EntityFrameworkCore`); Dapper for measured read hot paths |
| **PostgreSQL provider/driver** | `Npgsql.EntityFrameworkCore.PostgreSQL` over `Npgsql` |
| **Naming convention** | `EFCore.NamingConventions` (`UseSnakeCaseNamingConvention()`) |
| **Migrations** | EF Core Migrations + migrations bundles |
| **Valkey client** | `StackExchange.Redis` (Valkey is wire-compatible) |
| **Distributed cache abstraction** | `HybridCache` (`Microsoft.Extensions.Caching.Hybrid`) with `Microsoft.Extensions.Caching.StackExchangeRedis` |
| **Outbox/inbox** | The messaging library chosen per [ARCHITECTURE.md Section 9.4](./ARCHITECTURE.md), or a custom outbox via a `SaveChanges` interceptor |
| **Test databases** | Testcontainers for .NET (`Testcontainers.PostgreSql`, `Testcontainers.Redis`) + Respawn |

</DataAccessPractices>

---

<DatabaseTesting>
## 8. Database Testing Strategy

### 8.1 Mandatory requirements

- Every migration is applied in a CI pipeline before promotion (apply all migrations to an empty PostgreSQL container; also upgrade from the previous release's schema).
- Integration tests that touch the database use isolated, disposable PostgreSQL instances (Testcontainers) — never the EF Core in-memory or SQLite providers as a stand-in for PostgreSQL behavior.
- Tests do not depend on shared mutable state; each suite sets up and tears down its own data (Respawn between tests).
- Test data never contains real production data or PII.

### 8.2 What to test

| Concern | Required Test Coverage |
| --- | --- |
| **Migrations** | Forward migration applies cleanly on an empty database and on the previous schema; model snapshot matches the database (`dotnet ef migrations has-pending-model-changes`). |
| **Constraints** | Unique, foreign key, check, and not-null constraints reject invalid data. |
| **Soft-Delete Behavior** | Queries exclude soft-deleted rows by default; audit queries include them; partial unique indexes allow reuse of a key after soft delete. |
| **Traceability Fields** | `created_at`, `updated_at`, `deleted_at` are populated automatically. |
| **Concurrency** | Concurrent updates to an aggregate root raise a conflict; concurrent stock reservations never oversell. |
| **Outbox/Inbox** | The state change and outbox row commit atomically; the same event processed twice produces one side effect. |
| **Indexes / query plans** | Critical queries use expected indexes; query counts are asserted on critical paths (no N+1). |
| **Schema ownership** | Architecture test: no cross-schema access ([Section 5](#5-schema-ownership-per-bounded-context)). |
| **Boundary Conditions** | Large payloads, maximum lengths, and edge-case values. |

### 8.3 Test isolation rules

- Reuse one container per test run (collection fixture) for speed, but reset data between tests.
- Seed data through explicit factories/builders, not drifting seed scripts.
- Cache tests run against a real Valkey container, including an outage scenario.

</DatabaseTesting>

---

<DataGovernance>
## 9. Data Governance and Compliance

### 9.1 Mandatory requirements

- Identify and classify personal data (PII), sensitive data, and regulated data during schema design (customer names, e-mails, phone numbers, addresses, tax IDs such as CPF/CNPJ, IP addresses, payment references).
- Implement data retention policies at the database layer (scheduled cleanup jobs or partition-based lifecycle).
- Support the right to erasure (LGPD Art. 18, GDPR Art. 17) through anonymization or deletion procedures for personal data, with audit logging — while preserving fiscal/legal records (orders, invoices) for their legally required retention period by anonymizing the personal fields rather than deleting the financial record.
- Apply masking or pseudonymization in non-production environments; production data is never copied to development/test without anonymization.
- **Never store card numbers (PAN), CVV, or full magnetic-stripe data.** Payment data is tokenized by the payment provider; the database stores only provider references, last four digits, brand, and expiry as allowed by the provider's agreement (PCI DSS scope minimization).

### 9.2 Compliance rules

| Regulation / Standard | Database Impact |
| --- | --- |
| **LGPD / GDPR** | Support data subject access, portability, rectification, and erasure; record lawful basis and consent where applicable. |
| **PCI DSS** | Keep cardholder data out of the system (tokenization); restrict access to payment-related tables to dedicated service accounts; encrypt in transit and at rest. |

### 9.3 Recommended practices

- Document data lineage for regulated fields (origin, transformations, storage location, retention).
- Use column-level (application-level) encryption for highly sensitive fields (e.g., tax ID) rather than relying solely on disk encryption, with the algorithm mandated in [SECURITY.md](./SECURITY.md) Section 4.1 (ChaCha20-Poly1305); use a keyed hash (blind index) when exact-match lookup is needed.
- Review data governance periodically against retention and classification policies.
- Cross-reference [SECURITY.md](./SECURITY.md) Section 5 for credential and connection-string handling.

</DataGovernance>

---

<OperationsAndObservability>
## 10. Operations, Observability, and Resilience

### 10.1 Hosting

- Prefer managed PostgreSQL (and managed Valkey/Redis-compatible service) over self-managed infrastructure where operational overhead justifies it. If self-hosted, follow [CONTAINERS.md Section 7.4](./CONTAINERS.md).
- Databases are reachable only on private networks; never exposed to the public internet. Encrypt at rest and in transit.
- Configure automated backups with point-in-time recovery according to RPO/RTO targets; use IAM/managed-identity authentication where the provider supports it.

### 10.2 Mandatory metrics

- Database operation latency (p50, p95, p99) and error rate by type (timeout, deadlock, constraint violation, concurrency conflict).
- Connection pool utilization and wait time.
- Cache hit/miss rate, evictions, and Valkey latency.
- Outbox backlog (unprocessed rows, oldest age) and inbox duplicate rate.
- Storage growth, table/index bloat, replication lag.

### 10.3 Logging and diagnostics

- Log critical database operations with correlation/trace IDs.
- Never log credentials, sensitive payloads, or personal data; parameter values are not logged outside local development.
- Enable query logging only at a diagnostic level that does not expose sensitive information.

### 10.4 Backup and recovery

- Define a formal backup and retention policy with periodic restore testing (at least quarterly).
- Validate RPO/RTO targets; run recovery drills in an isolated environment.

### 10.5 Resilience patterns

| Pattern | Mandatory Behavior |
| --- | --- |
| **Retry with Backoff** | Retries with exponential backoff and jitter for transient errors (EF Core execution strategy; Polly for other calls). |
| **Circuit Breaker** | Prevent cascading failures when the database is degraded or unreachable. |
| **Connection Pool Recovery** | Npgsql discards broken connections; keep `Keepalive`/`Connection Idle Lifetime` tuned. |
| **Failover Readiness** | For replicated deployments, validate failover and client reconnection regularly. |
| **Read Replica Routing** | Route read-only queries to replicas only when the use case tolerates replication lag; document maximum staleness per use case. |
| **Graceful Degradation** | Define fallback behavior when PostgreSQL is unavailable (fail fast with `503`; queue via outbox only for commands that can be deferred). When Valkey is unavailable, bypass the cache. |

Additional rules:

- Health checks verify actual database connectivity ([CONTAINERS.md Section 8.1](./CONTAINERS.md)).
- Test failure scenarios (connection loss, failover, disk full, high latency) in pre-production.
- Avoid silent data loss: every failed write is logged, alerted, or queued for retry.

</OperationsAndObservability>

---

<DefinitionOfDone>
## 11. Database Definition of Done

A delivery that impacts the database is complete only when:

1. The data model meets functional and non-functional requirements and stays inside its context's schema ([Section 5](#5-schema-ownership-per-bounded-context)).
2. Every impacted entity contains `id`, `created_at`, `updated_at`, and `deleted_at`; every impacted aggregate-root table has a `version` column enforced on every update.
3. A descriptively named EF Core migration was created, reviewed (including its generated SQL), and versioned in the repository.
4. The migration follows expand/contract and is safe for rolling deployments; a rollback or forward-fix plan exists.
5. Critical queries were reviewed with execution-plan evidence and performance validation; no N+1 queries.
6. Applicable security requirements from [SECURITY.md](./SECURITY.md) are respected (least-privilege roles, TLS, no secrets or PII in logs).
7. EF Core usage follows [Section 7](#7-ef-core-and-data-access-practices): `IEntityTypeConfiguration` mappings, parameterized SQL only, soft-delete filters, concurrency tokens.
8. State changes that publish events use the transactional outbox; retriable consumers/handlers use the inbox/idempotency table.
9. Database integration tests (Testcontainers) validate migrations, constraints, traceability, soft delete, concurrency, and outbox/inbox behavior.
10. Personal and regulated data are classified, and retention/erasure needs are addressed; no card data is stored.
11. Caching (if applicable) is documented with TTL, invalidation, and fallback behavior, and the system works with Valkey down.
12. Resilience patterns (retry, circuit breaker, timeouts) are in place for critical database operations.

Any exception must be documented with owner, scope, risk, rationale, and expiration date.

</DefinitionOfDone>

---

_Last updated: September 30, 2026_
_These standards must be reviewed and updated at minimum once per year or after any significant database incident._

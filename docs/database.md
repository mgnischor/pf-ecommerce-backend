# Database

How the platform persists its state: **PostgreSQL** through **Entity Framework Core** and **Npgsql**, following
`ai/DATABASE.md`. This page is the operating manual; the rules live in the standard.

## Layout

| Concern                                                                                                                            | Where                                                                                                 |
| ---------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| One schema and one `DbContext` per bounded context                                                                                 | `src/<Context>/Infrastructure/Persistence/<Context>DbContext.cs` (`identity`, `catalog`, `inventory`) |
| Mappings (`IEntityTypeConfiguration<T>`, no data annotations in the domain)                                                        | `src/<Context>/Infrastructure/Persistence/*Configuration.cs`                                          |
| Repositories (the ports defined in each Domain)                                                                                    | `src/<Context>/Infrastructure/Persistence/Ef*Repository.cs`                                           |
| Migrations and the model snapshot, with their own history table `<schema>.__ef_migrations_history`                                 | `src/<Context>/Infrastructure/Persistence/Migrations/`                                                |
| Shared base: schema, unit of work, mandatory columns, soft delete, version token, interceptors, outbox, inbox, relay, health check | `src/SharedKernel/Infrastructure/Persistence/`                                                        |
| Roles, schemas and grants                                                                                                          | `database/provision-roles.sql`, `database/provision.sh`                                               |
| Migrations bundles                                                                                                                 | `scripts/build-migrations-bundles.*`, `scripts/apply-migrations.sh`, Dockerfile target `migrations`   |

Contexts without entities yet (Billing, Cart, Checkout, Customers, Notifications, Ordering, Promotions, Reviews, Shipping)
have no `DbContext`: it is created, with its schema and first migration, together with the first entity of the context.
`PersistenceConventionTests` fails the build if a context's domain entity is not mapped, if a `DbContext` maps another
context's entity, or if any SQL names another context's schema.

## What every entity gets, automatically

`ModuleDbContext` applies `EntityModelConventions` to every `Entity` in the model, so no configuration class can forget it:

| Column                                                 | Source                                                                                                                                                                                      |
| ------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `id` `uuid`                                            | Generated in the domain (UUIDv7), `ValueGeneratedNever`                                                                                                                                     |
| `created_at`, `updated_at`, `deleted_at` `timestamptz` | The domain stamps them from `TimeProvider`; `AuditingSaveChangesInterceptor` keeps them true on every commit                                                                                |
| `version` `integer` (aggregate roots only)             | Incremented by the aggregate; EF Core uses it as the concurrency token on every `UPDATE`                                                                                                    |
| soft delete                                            | Removing an entity through a repository sets `deleted_at` (and bumps `version`); the global query filter `soft_delete` hides the row. Audit flows use `IgnoreQueryFilters(["soft_delete"])` |

Business keys that coexist with soft delete are **partial unique indexes** (`WHERE deleted_at IS NULL`): `users.email`,
`products.sku`, `inventory_items.sku`. Enums are stored as `text` with a `CHECK`; money is `numeric(19,4)` plus `char(3)`.

## One unit of work per context

Each handler receives the `IUnitOfWork` of **its own** context. There is no global `IUnitOfWork` registration; use cases
are registered with `services.AddUseCase<TContext, THandler>()`, which hands the context to the handler's `IUnitOfWork`
parameter. A use case ends with one `SaveChangesAsync`, which commits the aggregate, its outbox rows and its inbox row
atomically. Repositories only stage changes; `Update` accepts only aggregates loaded through the repository (a detached
one would bypass the version check).

## Concurrency and conflicts

A lost optimistic-concurrency race (`version` no longer matches) or a unique key taken by a concurrent request surfaces as
`PersistenceConflictException`, which `PersistenceConflictMiddleware` answers as **409 Conflict** with
`code: CONCURRENT_UPDATE` or `DUPLICATE_RECORD` and nothing from the failed statement. Endpoints that take `If-Match`
answer **412** earlier, from the handler, when the client's version is already stale.

## Transactional outbox, relay, inbox

- `OutboxSaveChangesInterceptor` converts the domain events raised by the tracked aggregates into `<schema>.outbox_messages`
  rows **in the same transaction** as the state change, and clears the events only after a successful commit.
  Columns: `id` (event id), `type`, `payload` `jsonb`, `aggregate_id`, `aggregate_version`, `occurred_at`,
  `correlation_id`, `causation_id`, `processed_at`, `attempts`, `locked_until`, `last_error` (an exception type, never a message).
- `OutboxRelay<TContext>` publishes a context's outbox **at least once**. A batch is claimed with
  `SELECT … FOR UPDATE SKIP LOCKED` in a short transaction that only stamps a lease (`locked_until`); the broker call happens
  outside any transaction; a row whose publisher failed or whose instance died becomes claimable again when the lease expires;
  after `Outbox:Relay:MaxAttempts` it waits for an operator. Several instances can run side by side.
  **The relays run only where `Outbox:Relay:Enabled` is `true`** (the worker role): `Program` then registers RabbitMQ
  and one relay per context. Elsewhere events accumulate in the outbox. See [messaging.md](messaging.md), which also covers the consumers that use the inbox.
- `Inbox` (`IInbox`) registers `(consumer, message_id)` in `<schema>.inbox_messages` in the consumer's own transaction; the
  composite primary key makes a duplicate delivery impossible to commit. No consumer exists yet.

## Configuration

| Key                                                                                        | Meaning                                                                                                  |
| ------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------- |
| `ConnectionStrings:Postgres`                                                               | **Secret.** `ConnectionStrings__Postgres` or a mounted secret file. The host refuses to start without it |
| `Database:MaxPoolSize` (20), `CommandTimeoutSeconds` (30), `ConnectionTimeoutSeconds` (15) | One shared `NpgsqlDataSource`; `replicas × MaxPoolSize` must stay below the server's `max_connections`   |
| `Database:MaxRetryCount` (3), `MaxRetryDelaySeconds` (5)                                   | EF Core retrying execution strategy for transient failures                                               |
| `Database:MigrateOnStartup` (false)                                                        | Applies pending migrations at start. **Honoured only in the Development environment**                    |
| `Outbox:Relay:*`                                                                           | Enabled (worker role only), poll interval, batch size, lease, maximum attempts of the relay              |

`/health/ready` runs `SELECT 1` against PostgreSQL (3 s budget) and reports only that the database is unreachable, never why.
`/health/live` does not depend on it.

## Local development

```bash
docker compose -f docker-compose-dev.yml up --build   # migrates on start (Database__MigrateOnStartup)
```

Without Compose, point the API at any PostgreSQL 16+ and let it migrate itself in Development:

```powershell
$env:ConnectionStrings__Postgres = "Host=localhost;Database=ecommerce;Username=app;Password=..."
dotnet run --project Portfolio.csproj
```

## Changing the schema

```powershell
# 1. Change the entity/configuration, then create a descriptively named migration for that context.
#    --namespace is required: the default would derive it from the folder (Portfolio.src...).
dotnet ef migrations add AddIndexProductsName `
  --context CatalogDbContext --project Portfolio.csproj `
  --output-dir src/Catalog/Infrastructure/Persistence/Migrations `
  --namespace Portfolio.Catalog.Infrastructure.Persistence.Migrations

# 2. Read the SQL it will run. A migration is never merged unread.
dotnet ef migrations script --idempotent --context CatalogDbContext --project Portfolio.csproj

# 3. CI fails if the model and the snapshot disagree:
dotnet ef migrations has-pending-model-changes --context CatalogDbContext --project Portfolio.csproj
```

`dotnet-ef` is pinned in `.config/dotnet-tools.json` (`dotnet tool restore`). Follow expand/contract
(`ai/DATABASE.md §6.2`): never ship a schema change in the same release as code that cannot run without it, and give every
destructive migration a rollout and rollback plan. A **new bounded context** also needs: its `DbContext` (schema = the
context's name in snake_case), its design-time factory, its module (`Add<Context>Module`), its schema added to
`database/provision-roles.sql`, and an entry in the Compose migration chain.

## Deployment

Migrations are never applied by the API in production. The order, as Compose runs it (`docker-compose-prod.yml`):

1. `db-provision` creates `app_migrator`, `app_runtime`, `app_readonly` and the schemas (owned by `app_migrator`).
2. `db-migrate-identity` → `db-migrate-catalog` → `db-migrate-inventory` run one bundle each as `app_migrator`
   (image `ecommerce-migrations`, Dockerfile target `migrations`; the connection is the secret file named by
   `PF_DESIGN_TIME_CONNECTION_FILE`, never an argument).
3. `db-grant` re-runs the provisioning so the runtime and reporting roles are granted on the tables just created and
   denied the migration history.
4. The API starts and connects as `app_runtime`: `SELECT`, `INSERT`, `UPDATE` only, no DDL, no `DELETE` (rows are logically
   deleted; a purge job, when it exists, gets its own grant).

Outside Compose use `scripts/build-migrations-bundles.sh` and `scripts/apply-migrations.sh` (a Kubernetes Job runs the same
bundles). Every step is safe to repeat. Rollback of a release is a forward fix or the migration's `Down`, decided in its rollout plan.

## Tests

- **Unit** — options, connection handling, health check, outbox message, conflict exception (no database).
- **Architecture** — one schema and one `DbContext` per context, no foreign entity, mandatory columns, version token, soft-delete
  filter, snake_case, migrations present, no `FromSqlRaw`/`ExecuteSqlRaw`, no credential literal, no cross-schema SQL.
- **Integration (Testcontainers, real PostgreSQL 18, never the in-memory or SQLite providers)** — one container per run;
  every migration is applied once to a template database and each test gets its own clone, so tests share no state. Covers
  migrations (apply, repeat, undo, no pending changes), constraints, traceability and soft delete, concurrency, outbox,
  relay (including side-by-side instances), inbox, repositories, least-privilege roles, and the HTTP pipeline on top.
  Docker must be running.

## Not done yet

- **Valkey cache** (`ai/DATABASE.md §4`): nothing is cached; `AccessTokenValidator` reads the account on every authenticated
  request, which is what makes revocation immediate.
- **Telemetry**: Npgsql/EF Core instrumentation joins the OpenTelemetry bootstrap (`ai/OBSERVABILITY.md`).
- **Message broker**: the relay has no publisher yet (see above).
- **HTTP `Idempotency-Key` table**: only stock adjustments are idempotent today (by the key stored on the ledger line).
- **Purge jobs** for expired inbox rows and processed outbox rows, and retention/erasure procedures (`ai/DATABASE.md §9`).
- **TLS to PostgreSQL** is a property of the connection string (`SslMode=VerifyFull` outside local); the single-host Compose
  network is internal and unencrypted.

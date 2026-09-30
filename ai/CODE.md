# Code Quality and Performance Standards

> **Scope:** These standards apply to all code in the `pf-ecommerce-backend` repository: **C# on .NET 10**, ASP.NET Core, Entity Framework Core with Npgsql (PostgreSQL), Valkey, RabbitMQ, and OpenTelemetry. Every item marked as mandatory is a hard requirement unless a documented exception is approved by the maintainers. Performance and code quality are first-class engineering outcomes. All code and tests must also follow [SECURITY.md](./SECURITY.md).

---

## Table of Contents

1. [General Engineering Principles](#1-general-engineering-principles)
2. [Global Quality Gates](#2-global-quality-gates)
3. [Global Performance Baseline](#3-global-performance-baseline)
4. [C# and .NET Standards](#4-c-and-net-standards)
    - 4.1 [Architecture and Design Rules](#41-architecture-and-design-rules)
    - 4.2 [Performance Rules](#42-performance-rules)
    - 4.3 [Quality and Maintainability Rules](#43-quality-and-maintainability-rules)
    - 4.4 [Testing Requirements](#44-testing-requirements)
5. [Recommended Libraries and Tooling](#5-recommended-libraries-and-tooling)
6. [Code Review and Pull Request Standards](#6-code-review-and-pull-request-standards)
7. [Definition of Done for Code Quality and Performance](#7-definition-of-done-for-code-quality-and-performance)

---

<GeneralEngineeringPrinciples>
## 1. General Engineering Principles

| Principle | Mandatory Behavior |
| --- | --- |
| **Clarity Over Cleverness** | Prefer explicit, easy-to-understand code over compact but obscure logic. |
| **Measure Before Optimizing** | Never optimize on assumptions. Use profiling, benchmarks, and production telemetry. |
| **Fail Fast and Deterministically** | Validate inputs early and fail with meaningful errors. |
| **Single Responsibility** | Each module, class, and method has one primary reason to change. |
| **SOLID by Default** | SOLID is the mandatory design baseline for new code and refactoring. |
| **Deterministic Builds** | Build outputs are reproducible with pinned dependencies and locked restore. |
| **Observability by Default** | Critical paths expose the logs, metrics, and traces needed for diagnosis. |
| **Backward Compatibility by Contract** | Public APIs and event contracts evolve with explicit versioning and compatibility tests. |

### 1.1 Mandatory SOLID Application

- **S — Single Responsibility:** classes, modules, and methods have one clear responsibility.
- **O — Open/Closed:** prefer extension by composition/abstraction over modification of stable code.
- **L — Liskov Substitution:** subtype behavior remains substitutable without contract surprises.
- **I — Interface Segregation:** focused interfaces; no broad "do-everything" contracts.
- **D — Dependency Inversion:** high-level policy depends on abstractions, not infrastructure details.

Security-related code and tests are implemented in alignment with [SECURITY.md](./SECURITY.md), especially input validation, authentication/authorization, cryptography, secrets handling, and logging.

</GeneralEngineeringPrinciples>

---

<GlobalQualityGates>
## 2. Global Quality Gates

The following gates run in CI on every pull request:

| Gate | Requirement | Pipeline Policy |
| --- | --- | --- |
| Build | Clean `dotnet build -warnaserror` in `Release` | Any warning or error blocks merge |
| Static Analysis | .NET analyzers at `latest-recommended` plus the analyzers in [Section 5](#5-recommended-libraries-and-tooling) | `HIGH` severity issues block merge |
| Formatting | `csharpier check .` for C#; Prettier for JSON/YAML/Markdown (`.prettierrc`); `.editorconfig` respected | Non-compliant formatting blocks merge |
| Unit Tests | Fast deterministic suite for business logic | Failing tests block merge |
| Architecture Tests | Fitness functions from [ARCHITECTURE.md Section 10](./ARCHITECTURE.md) | Any violation blocks merge |
| Coverage | Minimum line and branch thresholds | Below threshold blocks merge |
| Dependency Health | No package with a known `HIGH`/`CRITICAL` vulnerability (CVSS ≥ 7.0), per [SECURITY.md Section 9](./SECURITY.md); `dotnet list package --vulnerable --include-transitive` | Blocks merge |

### 2.1 Baseline Thresholds

- **Line coverage:** minimum 80% for Domain and Application code.
- **Branch coverage:** minimum 70% for decision-heavy modules.
- **Mutation testing (recommended for core domain — Ordering, Billing, Inventory, Promotions, Checkout):** minimum 60% mutation score (Stryker.NET).
- **Flaky tests:** tolerated rate below 1%; flaky tests are quarantined and fixed ([TESTS.md Section 11](./TESTS.md)).

### 2.2 Security Gate Alignment

- CI security checks are aligned with [SECURITY.md](./SECURITY.md).
- Security suites (SAST, dependency scanning, secret scanning, security-focused integration tests) run on pull requests where applicable.
- Any violation classified as blocking in [SECURITY.md](./SECURITY.md) blocks merge like a functional gate.

### 2.3 Toolchain and Runtime Currency

- Production code targets only vendor-supported runtimes. An unsupported runtime is a security defect, not technical debt.
- Pin the SDK in `global.json` (`rollForward: latestFeature`) so every developer and CI job builds with the same version.
- Current targets (review on every standards update):

| Item | Target | Policy |
| --- | --- | --- |
| Runtime / SDK | **.NET 10 (LTS)** (`net10.0`) | LTS releases only; upgrade before the previous LTS reaches end of support. |
| Language | **C# 14** (the default for .NET 10) | Do not pin `LangVersion` below the SDK default. |
| Package versions | Central Package Management (`Directory.Packages.props`) | One version per package across the repository; lock files committed (`packages.lock.json`) and restored with `--locked-mode` in CI. |
| Build settings | `Directory.Build.props` | `Nullable=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild=true`, `Deterministic=true`, `ContinuousIntegrationBuild=true` in CI. |

- Use Renovate or Dependabot to keep the SDK, container base images, and packages current; group minor/patch updates and review majors individually.

</GlobalQualityGates>

---

<GlobalPerformanceBaseline>
## 3. Global Performance Baseline

The service defines and publishes SLO-aligned performance targets ([OBSERVABILITY.md](./OBSERVABILITY.md) Section 9). At minimum:

| Metric | Required Practice |
| --- | --- |
| Latency | Track p50, p95, p99 per endpoint and per consumer. |
| Throughput | Define minimum acceptable requests/messages per second under normal load. |
| Error Rate | Track and alert on functional errors and timeouts separately. |
| Resource Utilization | Track CPU, memory, GC pressure, thread-pool saturation, DB pool saturation, and queue depth. |

### 3.1 Mandatory Performance Discipline

- Benchmark critical code paths (pricing, cart totals, checkout, stock reservation) before and after significant changes.
- Use realistic datasets and traffic models for load testing.
- Treat p95/p99 regressions above 10% as release blockers unless explicitly waived.
- Keep performance test artifacts (scripts, datasets, reports) versioned with the codebase.

</GlobalPerformanceBaseline>

---

<DotNetStandards>
## 4. C# and .NET Standards

### 4.1 Architecture and Design Rules

- Enforce the layer boundaries of [ARCHITECTURE.md Section 6](./ARCHITECTURE.md): `API → Application → Domain ← Infrastructure`.
- Types are `internal` by default and `sealed` unless designed for inheritance. `public` requires a reason (published contract or test seam).
- Depend on abstractions at architectural boundaries; do not leak EF Core, ASP.NET Core, RabbitMQ, or Valkey types into Domain or Application.
- Keep classes small enough to be understood in one review pass. No god classes or `Utils`/`Helpers` dumps.
- Prefer immutable data: `record`/`readonly record struct` for value objects, events, DTOs, and contracts; `init`/`required` members for inbound models.
- Constructor injection only (primary constructors are fine). No service locator, no static access to services, no `IServiceProvider` injection outside composition/factory code.
- Register services through per-context `Add<Context>Module` extension methods; use the options pattern (`IOptions<T>`, `ValidateDataAnnotations().ValidateOnStart()`) for configuration.
- Avoid cyclic dependencies between namespaces and contexts.
- Keep transactional boundaries explicit and minimal (one transaction per use case).

### 4.2 Performance Rules

| Area | Mandatory Recommendation |
| --- | --- |
| Async I/O | Use async APIs for all I/O; never sync-over-async (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`). Pass and honor `CancellationToken` (`HttpContext.RequestAborted` at the edge). Return `ValueTask` only when measurements justify it. |
| Allocation Pressure | Minimize avoidable allocations in hot paths; use `Span<T>`, `ArrayPool<T>`, `stackalloc` (bounded) where safe and measured. |
| Collections | Choose by access pattern (`Dictionary`, `HashSet`, `FrozenDictionary` for read-mostly lookup tables). |
| LINQ | Avoid complex chained LINQ in hot loops when profiling shows overhead. Never enumerate an `IQueryable` more than once. |
| Strings | `StringBuilder` or span-based APIs for repeated concatenation; `[GeneratedRegex]` instead of `new Regex` at runtime; `LoggerMessage` source generation for hot log statements. |
| Serialization | `System.Text.Json` with source generation (`JsonSerializerContext`) for contracts and events. |
| Concurrency | Bound parallelism (`Parallel.ForEachAsync` with `MaxDegreeOfParallelism`, `SemaphoreSlim`, bounded `Channel<T>`). No unbounded `Task.Run` or fire-and-forget; background work runs in `BackgroundService`/`IHostedService`. |
| Database (EF Core / Npgsql) | No N+1 queries; project reads with `Select` and `AsNoTracking`; paginate every list (keyset pagination for large sets); use `ExecuteUpdateAsync`/`ExecuteDeleteAsync` for set-based changes; `AsSplitQuery` for multiple collection includes; one `DbContext` per scope, never shared across threads; never call `SaveChanges` in a loop. Share a single `NpgsqlDataSource` per database and size the pool from measurement. |
| Caching (Valkey) | Cache only with an explicit TTL and invalidation strategy; never cache without eviction; guard against stampedes (`HybridCache` with stampede protection or per-key locking); never cache authorization decisions or personal data without a documented reason. Cache values must be versioned or tolerant of schema change. |
| Messaging (RabbitMQ) | Use publisher confirms for outbox relay, bounded consumer prefetch (`BasicQos`), manual acks after successful processing, and a dead-letter exchange for poison messages. Reuse one connection per process and channels per thread/consumer. |
| HTTP clients | Use `IHttpClientFactory`/typed clients with `Microsoft.Extensions.Http.Resilience`; explicit timeouts on every outbound call. |

#### .NET Runtime Guidance

- Monitor GC metrics (Gen2 collections, pause time), thread-pool queue length, and exceptions per second via OpenTelemetry runtime instrumentation.
- Use Server GC only when the container CPU limit justifies it; set `DOTNET_GCHeapHardLimit` (or rely on the container memory limit) deliberately.
- Enable `ReadyToRun`/tiered PGO defaults; consider Native AOT only with an ADR (EF Core and reflection-heavy libraries have constraints).

### 4.3 Quality and Maintainability Rules

| Topic | Guidance |
| --- | --- |
| Null Safety | `<Nullable>enable</Nullable>`; nullable warnings are errors. No `!` (null-forgiving) without a justification comment. |
| Function Complexity | Cyclomatic complexity ≤ **10** per method (analyzer `CA1502` with a code-metrics config, or SonarAnalyzer). |
| Exceptions | Exceptions are for exceptional flows. Expected business failures return a `Result` ([BUSINESS.md Section 5.2](./BUSINESS.md)). Never swallow exceptions: empty or log-only `catch` blocks are forbidden. Rethrow with `throw;`, never `throw ex;`. |
| Logging | Structured logging via `ILogger<T>` with message templates (never string interpolation), trace/correlation IDs, and no sensitive data ([SECURITY.md Section 11](./SECURITY.md)). |
| API Contracts | Validate external input at the boundary; return typed Problem Details responses. |
| Naming | Domain language; no ambiguous abbreviations. Async methods end in `Async`. Interfaces start with `I`. |
| Documentation | Public types and members carry XML doc comments (`///`) describing purpose, parameters, and exceptions. Comments explain *why*, not *what*. |
| Time | Inject `TimeProvider`; never read `DateTime.UtcNow`/`DateTimeOffset.UtcNow` directly ([BUSINESS.md Section 9.1](./BUSINESS.md)). |
| Randomness / IDs | Security-relevant randomness uses `RandomNumberGenerator`; entity IDs are generated in the domain (`Guid.CreateVersion7()` or a typed ID), never by the database default where the domain needs the ID before persistence. |
| Money | `decimal` only; never `double`/`float` ([BUSINESS.md Section 7.1](./BUSINESS.md)). |

#### Language Notes

- Use file-scoped namespaces, `var` where the type is obvious, pattern matching and `switch` expressions (exhaustive), collection expressions (`[]`), primary constructors for simple DI, and `required` members.
- Prefer `sealed record` for domain events, commands, queries, and contracts; `sealed class` for aggregates and entities.
- Formatting: CSharpier for C# (4-space indent, 120 columns, LF line endings); Prettier for non-C# files. Format-on-save is configured in `workspace/Portfolio.code-workspace`.
- No `#pragma warning disable` or `[SuppressMessage]` without an inline justification and, for security or correctness rules, an exception record.

#### Commercially Licensed Packages

Several widely used .NET packages moved to commercial licenses: **MediatR 13+**, **AutoMapper 15+**, **MassTransit 9+**, and **FluentAssertions 8+** (Duende IdentityServer has always been commercial). Introducing or upgrading into these versions requires license approval per [SECURITY.md Section 9](./SECURITY.md). Preferred alternatives:

| Need | Preferred alternative |
| --- | --- |
| In-process mediator | Direct injection of handler interfaces (no mediator), a source-generated `Mediator` (MIT), or Wolverine |
| Object mapping | Mapperly (source-generated) or explicit mapping methods |
| Messaging / outbox | Wolverine, Rebus, DotNetCore.CAP, or raw `RabbitMQ.Client` with a custom outbox relay |
| Test assertions | Shouldly or AwesomeAssertions — see [TESTS.md Section 9](./TESTS.md) |

### 4.4 Testing Requirements

- Unit tests cover domain invariants, validation logic, and error behavior.
- Integration tests cover PostgreSQL (EF Core), RabbitMQ, Valkey, and external API boundaries, using real services in containers (Testcontainers).
- Contract tests are required for public HTTP APIs (OpenAPI) and integration-event schemas.
- Architecture tests enforce [ARCHITECTURE.md Section 10](./ARCHITECTURE.md).
- Performance regression tests are required for critical endpoints and batch jobs.
- Security controls implemented in code are tested per [SECURITY.md](./SECURITY.md).

</DotNetStandards>

---

<RecommendedLibraries>
## 5. Recommended Libraries and Tooling

Defaults that satisfy each requirement of this document. Domain-specific libraries are listed in the owning document: cryptography and security tooling in [SECURITY.md Sections 4.4 and 14](./SECURITY.md), test tooling in [TESTS.md](./TESTS.md), data access in [DATABASE.md Section 7](./DATABASE.md), business-rule libraries in [BUSINESS.md Section 14](./BUSINESS.md), architecture fitness functions and messaging in [ARCHITECTURE.md Sections 9.4 and 10.3](./ARCHITECTURE.md), API contract tooling in [API_CONTRACTS.md Section 13](./API_CONTRACTS.md), container tooling in [CONTAINERS.md Section 13](./CONTAINERS.md), and telemetry libraries in [OBSERVABILITY.md Section 16](./OBSERVABILITY.md).

A library other than the default is allowed when it is actively maintained, license-approved, and recorded in an ADR. Mixing two libraries for the same purpose is not allowed.

| Requirement | Default |
| --- | --- |
| **Formatter (gate)** | CSharpier (C#); Prettier (JSON, YAML, Markdown) |
| **Linter / analyzers (gate)** | .NET analyzers (`latest-recommended`), SonarAnalyzer.CSharp, Roslynator, Meziantou.Analyzer; `Microsoft.CodeAnalysis.BannedApiAnalyzers` to ban `DateTime.UtcNow`, `Thread.Sleep`, `new Random()`, etc. |
| **Type / null safety** | Nullable reference types |
| **Complexity gate (≤ 10)** | `CA1502` (code-metrics config) or SonarAnalyzer `S1541` |
| **Structured logging** | `Microsoft.Extensions.Logging` with source-generated `LoggerMessage`; exported via the OpenTelemetry logging provider (Serilog only with an ADR) |
| **Traces and metrics** | OpenTelemetry .NET (`OpenTelemetry.Extensions.Hosting`, ASP.NET Core / HttpClient / Npgsql / runtime instrumentation) |
| **Input validation** | FluentValidation (DataAnnotations for trivial request models) |
| **Dependency injection** | `Microsoft.Extensions.DependencyInjection` |
| **Resilience** | `Microsoft.Extensions.Http.Resilience` / `Microsoft.Extensions.Resilience` (Polly v8) |
| **Serialization** | `System.Text.Json` with source generation |
| **Object mapping** | Mapperly or explicit mapping methods |
| **Expected-failure results** | Own `Result`/`Result<T>` in `SharedKernel` (or `ErrorOr`/`FluentResults` with approval) |
| **ORM / database driver** | EF Core + Npgsql (`Npgsql.EntityFrameworkCore.PostgreSQL`); Dapper only for measured read-path hot spots, with an ADR |
| **Cache client** | `Microsoft.Extensions.Caching.Hybrid` over `Microsoft.Extensions.Caching.StackExchangeRedis` (Valkey is wire-compatible); other Valkey clients only with an ADR |
| **Message broker client** | `RabbitMQ.Client` or a messaging library chosen per [ARCHITECTURE.md Section 9.4](./ARCHITECTURE.md) |
| **API documentation** | `Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore` (already referenced) |
| **Profiling / benchmarking** | BenchmarkDotNet, `dotnet-trace`, `dotnet-counters`, `dotnet-gcdump` |
| **Dependency updates** | Renovate or Dependabot |

</RecommendedLibraries>

---

<CodeReviewAndPRStandards>
## 6. Code Review and Pull Request Standards

Every PR is reviewed for correctness and non-functional quality.

### 6.1 Mandatory Review Checklist

- Is the behavior correct under normal, edge, and failure conditions?
- Does the design comply with SOLID and the layer/context boundaries?
- Are performance-sensitive changes accompanied by measurements?
- Does the change increase coupling or architectural debt?
- Are tests sufficient, deterministic, and meaningful?
- Are security requirements and tests aligned with [SECURITY.md](./SECURITY.md)?
- Are logs, metrics, and traces adequate for production diagnosis?
- Are naming, documentation, and structure clear for future maintenance?
- Are new dependencies from the recommended set in [Section 5](#5-recommended-libraries-and-tooling), with an approved license and a justification in the PR description?
- Is every state-changing handler/consumer idempotent, and is event publication done through the outbox?
- For schema changes: is there a reversible, reviewed EF Core migration ([DATABASE.md Section 6](./DATABASE.md))?

### 6.2 PR Size and Structure

- Prefer PRs under 500 changed lines.
- Separate refactoring from behavior changes where feasible.
- Include benchmark or load-test evidence for performance claims.
- Include migration notes for schema, API, or contract-impacting changes.
- Use Conventional Commits for commit messages; reference the business rule ID (`BR-ORD-001`) when a rule changes.

</CodeReviewAndPRStandards>

---

<DefinitionOfDone>
## 7. Definition of Done for Code Quality and Performance

A change is complete only when all items below are true:

1. Functional requirements are met and validated by automated tests.
2. Quality gates pass with no blocking findings.
3. Performance impact is measured for critical paths.
4. Operational visibility (logs/metrics/traces) is sufficient.
5. Documentation for behavior, contracts, and constraints is updated (including OpenAPI annotations).
6. SOLID principles are respected in design and implementation.
7. Security requirements and tests are aligned with [SECURITY.md](./SECURITY.md).
8. Review feedback is resolved with clear rationale for any accepted trade-off.
9. The code targets the supported runtime per [Section 2.3](#23-toolchain-and-runtime-currency), and any new dependency is license-approved and drawn from [Section 5](#5-recommended-libraries-and-tooling) or justified in an ADR.

Any exception to these standards must be documented with owner, scope, risk, rationale, and expiration date.

</DefinitionOfDone>

---

_Last updated: September 30, 2026_
_These standards must be reviewed and updated at minimum once per year or after any significant code quality incident._

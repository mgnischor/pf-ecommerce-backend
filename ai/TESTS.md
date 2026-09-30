# Testing Standards

> **Scope:** These standards apply to all test code of the `pf-ecommerce-backend` repository: a **C# / .NET 10** modular monolith exposing an HTTP API, persisting to **PostgreSQL** (EF Core/Npgsql), caching in **Valkey**, and messaging over **RabbitMQ**. Every item marked as mandatory is a hard requirement unless a documented exception is approved by the maintainers. Testing is a first-class engineering discipline — tests are deterministic, fast, isolated, and aligned with domain behavior. All test code also follows [SECURITY.md](./SECURITY.md).

---

## Table of Contents

1. [General Principles](#1-general-principles)
2. [Test Classification and Pyramid](#2-test-classification-and-pyramid)
    - 2.1 [Test Types](#21-test-types)
    - 2.2 [Test Pyramid Strategy](#22-test-pyramid-strategy)
    - 2.3 [Test Project Layout](#23-test-project-layout)
3. [Unit Testing Standards](#3-unit-testing-standards)
    - 3.1 [Universal Unit Test Rules](#31-universal-unit-test-rules)
    - 3.2 [Domain and Business Rule Unit Tests](#32-domain-and-business-rule-unit-tests)
    - 3.3 [Test Naming and Structure](#33-test-naming-and-structure)
4. [Integration Testing Standards](#4-integration-testing-standards)
    - 4.1 [Universal Integration Test Rules](#41-universal-integration-test-rules)
    - 4.2 [Database Integration Tests](#42-database-integration-tests)
    - 4.3 [API Integration Tests](#43-api-integration-tests)
    - 4.4 [Messaging and Event Integration Tests](#44-messaging-and-event-integration-tests)
    - 4.5 [Cache Integration Tests](#45-cache-integration-tests)
5. [Contract Testing Standards](#5-contract-testing-standards)
6. [End-to-End Testing Standards](#6-end-to-end-testing-standards)
7. [Performance and Load Testing Standards](#7-performance-and-load-testing-standards)
8. [Security Testing Standards](#8-security-testing-standards)
9. [.NET Test Stack](#9-net-test-stack)
    - 9.1 [Frameworks and Tooling](#91-frameworks-and-tooling)
    - 9.2 [Unit Test Patterns](#92-unit-test-patterns)
    - 9.3 [Integration Test Patterns](#93-integration-test-patterns)
    - 9.4 [Architecture Test Patterns](#94-architecture-test-patterns)
    - 9.5 [Performance Test Patterns](#95-performance-test-patterns)
10. [Test Data Management](#10-test-data-management)
11. [Flaky Test Policy](#11-flaky-test-policy)
12. [Coverage and Quality Gates](#12-coverage-and-quality-gates)
13. [CI/CD Test Pipeline Integration](#13-cicd-test-pipeline-integration)
14. [Testing Definition of Done](#14-testing-definition-of-done)

---

<GeneralPrinciples>
## 1. General Principles

| Principle | Mandatory Behavior |
| --- | --- |
| **Determinism** | Tests produce the same result every time, regardless of environment, order, or time of day. |
| **Isolation** | Tests do not share mutable state. Each test sets up its own preconditions and cleans up after itself. |
| **Speed** | Unit tests run in milliseconds. Integration tests are fast enough to run in CI without blocking developer flow. |
| **Readability** | Tests are documentation. Names, structure, and assertions are immediately understandable. |
| **Domain Alignment** | Tests use domain language and reflect business behavior, not implementation details. See [BUSINESS.md](./BUSINESS.md). |
| **Independence** | Tests do not depend on execution order. |
| **Single Reason to Fail** | Each test verifies one logical behavior. |
| **No Infrastructure in Unit Tests** | Unit tests use no database, network, file system, or real clock. Use fakes and stubs. |
| **Security as a Test Concern** | Security controls are tested, not assumed. See [SECURITY.md](./SECURITY.md). |
| **Traceability** | Tests are traceable to a requirement, user story, or business rule (`BR-ORD-001` in the test class or trait). |

</GeneralPrinciples>

---

<TestClassification>
## 2. Test Classification and Pyramid

### 2.1 Test Types

| Type | Scope | Speed | Infrastructure Required |
| --- | --- | --- | --- |
| **Unit** | A single class or small cluster of domain/application types in isolation. | < 50 ms | None |
| **Architecture** | Structural rules: dependency direction, context boundaries, naming ([ARCHITECTURE.md Section 10](./ARCHITECTURE.md)). | < 2 s | None |
| **Integration** | Interaction with PostgreSQL, Valkey, RabbitMQ, or the HTTP pipeline. | < 5 s | Testcontainers |
| **Contract** | OpenAPI and integration-event schema compatibility. | < 2 s | None or stub server |
| **End-to-End (E2E)** | A critical business flow through the running API and all backing services. | < 30 s | Full environment (Compose/staging) |
| **Performance / Load** | Throughput, latency, and resource utilization under load. | Variable | Dedicated environment |
| **Security** | SAST, DAST, dependency scanning, and security-specific functional tests. | Variable | Depends on type |

### 2.2 Test Pyramid Strategy

```
         /  E2E  \            ← Few: critical business flows only
        /----------\
       / Integration \        ← Moderate: every boundary
      /----------------\
     /    Unit Tests     \    ← Many: every business rule
    /______________________\
```

| Layer | Target Distribution | Rationale |
| --- | --- | --- |
| **Unit** | 70–80% | Fast, cheap, cover the highest volume of business logic. |
| **Integration** | 15–25% | Validate boundaries without duplicating unit-level logic. |
| **E2E** | 5–10% | Critical paths only; expensive and slow. |

- Inverting the pyramid is a defect to be corrected incrementally.
- Architecture and contract tests are orthogonal to the pyramid and are always required.

### 2.3 Test Project Layout

Test projects live under `tests/` and mirror the source structure by Bounded Context and aggregate ([ARCHITECTURE.md Section 2.3](./ARCHITECTURE.md)):

```
tests/
├── Portfolio.UnitTests/            # Domain + Application, per context: Ordering/Domain/Order/OrderTests.cs
├── Portfolio.ArchitectureTests/    # NetArchTest/ArchUnitNET fitness functions
├── Portfolio.IntegrationTests/     # Testcontainers: PostgreSQL, Valkey, RabbitMQ, WebApplicationFactory
├── Portfolio.ContractTests/        # OpenAPI snapshot/breaking-change and event-schema tests
├── Portfolio.E2ETests/             # Full-stack business flows (Compose/staging)
└── Portfolio.Benchmarks/           # BenchmarkDotNet
```

- Test projects see `internal` types through `[assembly: InternalsVisibleTo("Portfolio.UnitTests")]` (and equivalents) in the application project. This is the sanctioned exception to "no production code changes for testability".
- `Program` exposes `public partial class Program;` so `WebApplicationFactory<Program>` can host the application.

</TestClassification>

---

<UnitTestingStandards>
## 3. Unit Testing Standards

### 3.1 Universal Unit Test Rules

| Rule | Mandatory Behavior |
| --- | --- |
| **No external dependencies** | No databases, APIs, file systems, or system clock. Inject abstractions (`TimeProvider`, repositories) and use fakes. |
| **Arrange-Act-Assert (AAA)** | Clear separation between setup, execution, and verification. |
| **One logical assertion per test** | Multiple assertions only when verifying facets of one outcome. |
| **No test interdependence** | No reliance on state left by another test; shared setup is idempotent and side-effect-free. |
| **No conditional logic in tests** | No `if`, `switch`, or hand-written loops. Use `[Theory]` with `[InlineData]`/`[MemberData]` for multiple cases. |
| **Explicit expected values** | Assert against literal values or well-named constants, not computed expectations. |
| **No production code modification** | Tests must not require making members public solely for testability (see the `InternalsVisibleTo` exception in [Section 2.3](#23-test-project-layout)). |

### 3.2 Domain and Business Rule Unit Tests

These extend [BUSINESS.md Section 12](./BUSINESS.md):

| Concern | Required Coverage |
| --- | --- |
| **Invariants** | Every aggregate invariant has tests verifying enforcement and rejection of violation attempts. |
| **State transitions** | Every valid and every invalid transition is tested (Order, Payment, Shipment, Reservation, Cart, Refund). |
| **Calculations and derivations** | Known-answer tests covering zero, minimum, maximum, boundary, negative values, rounding boundaries, and currency mismatch. |
| **Validation rules** | Valid, invalid, and boundary inputs. |
| **Domain events** | Correct events are raised on state changes; none on failed operations. |
| **Authorization rules** | Permitted and denied scenarios per role, including resource ownership. |
| **Temporal rules** | `FakeTimeProvider` covering boundaries (coupon validity, reservation expiry, cart expiry). |
| **Idempotency** | Handlers that may be retried are invoked twice; the second invocation produces no duplicate side effect. |

### 3.3 Test Naming and Structure

| Convention | Pattern | Example |
| --- | --- | --- |
| **Behavior-driven (default)** | `Should_{expected_behavior}_when_{condition}` | `Should_reject_order_when_total_is_below_minimum` |
| **Given-When-Then** | `Given_{context}_when_{action}_then_{outcome}` | `Given_expired_coupon_when_applied_then_returns_error` |

Rules:

- Use domain language, not technical jargon.
- Group tests in classes named after the domain concept and concern: `OrderStateTransitionTests`, `OrderTotalCalculationTests` — not one giant `OrderTests`.
- Tag tests with the business rule they verify: `[Trait("Rule", "BR-ORD-001")]`.

</UnitTestingStandards>

---

<IntegrationTestingStandards>
## 4. Integration Testing Standards

### 4.1 Universal Integration Test Rules

| Rule | Mandatory Behavior |
| --- | --- |
| **Test real boundaries** | Exercise the real PostgreSQL, Valkey, RabbitMQ, and HTTP pipeline — not in-memory substitutes (EF Core InMemory/SQLite providers are forbidden for PostgreSQL behavior). |
| **Isolated infrastructure** | Disposable Testcontainers, one set per test run (collection fixture). Never test against shared environments. |
| **Independent data** | Each test creates its own data; Respawn resets state between tests. No reliance on pre-seeded shared data. |
| **Deterministic ordering** | No dependence on execution order; use unique identifiers to avoid collisions. |
| **Timeout protection** | Explicit timeouts (xUnit `Timeout`, `CancellationTokenSource`) on all integration tests. |
| **Failure diagnostics** | On failure, output actionable diagnostics (response body, logs, container logs). |
| **Pinned images** | Container images are pinned to versions matching production (`postgres:17-alpine`, the chosen Valkey and RabbitMQ versions). |

### 4.2 Database Integration Tests

These comply with [DATABASE.md Section 8](./DATABASE.md):

| Concern | Required Coverage |
| --- | --- |
| **Migrations** | Forward migration applies on an empty database and on the previous schema; the model has no pending changes. |
| **Constraints** | Unique, foreign key, check, and not-null constraints reject invalid data. |
| **Soft-delete behavior** | Queries exclude soft-deleted rows by default; audit queries include them; partial unique indexes behave. |
| **Traceability fields** | `created_at`, `updated_at`, `deleted_at` are populated automatically. |
| **Indexes** | Critical queries use expected indexes; query counts asserted on critical paths (no N+1). |
| **Concurrency** | Optimistic concurrency conflicts on aggregate roots are detected; concurrent stock reservations never oversell. |
| **Transactions** | Commit persists, rollback reverts; one aggregate per transaction. |
| **Outbox** | The state change and the outbox row are committed atomically. |
| **Schema ownership** | A context's `DbContext` maps only its own schema ([DATABASE.md Section 5](./DATABASE.md)). |

### 4.3 API Integration Tests

| Concern | Required Coverage |
| --- | --- |
| **HTTP status codes** | Success, validation failure (`400`/`422`), unauthenticated (`401`), forbidden (`403`), not found (`404`), conflict (`409`), server error. |
| **Request/response schemas** | Bodies match the documented OpenAPI contract. |
| **Authentication/Authorization** | Unauthenticated and unauthorized requests are rejected for **every** protected endpoint and role; ownership (BOLA/IDOR) is tested: customer A cannot access customer B's resources. |
| **Error responses** | RFC 9457 Problem Details with `ruleId`/`code`; no internal details leaked. |
| **Idempotency** | Repeating a request with the same `Idempotency-Key` returns the original result without a second side effect; reuse with a different payload is rejected. |
| **Pagination and filtering** | Boundary cases (empty, single, maximum page size); deterministic ordering. |
| **Rate limiting** | Limited endpoints return `429` with `Retry-After`. |
| **Mass assignment** | Unknown or forbidden properties (`role`, `price`, `status`) in request bodies are rejected or ignored. |

### 4.4 Messaging and Event Integration Tests

| Concern | Required Coverage |
| --- | --- |
| **Message production** | Domain actions produce the correct integration events (schema, payload, event ID, correlation/causation IDs) via the outbox relay. |
| **Message consumption** | Consumers process messages correctly and handle malformed messages gracefully. |
| **Idempotency** | Processing the same message twice produces no duplicate side effect (inbox deduplication by event ID). |
| **Ordering** | Correct behavior when messages arrive out of order (where relevant: e.g., `PaymentCaptured` before `OrderPlaced` is processed). |
| **Dead-letter handling** | Unprocessable messages are routed to the dead-letter queue with diagnostic information after the configured retries. |
| **Redelivery** | A consumer that fails before acknowledging gets the message redelivered and ends in a consistent state. |

### 4.5 Cache Integration Tests

| Concern | Required Coverage |
| --- | --- |
| **Cache-aside** | A miss loads from PostgreSQL and populates Valkey with the documented TTL; a hit does not query PostgreSQL. |
| **Invalidation** | The documented invalidation event evicts the affected keys. |
| **Outage tolerance** | With Valkey stopped, requests still succeed (degraded) and no error reaches the client. |
| **Stampede protection** | Concurrent misses for the same key cause a single load. |

</IntegrationTestingStandards>

---

<ContractTestingStandards>
## 5. Contract Testing Standards

The HTTP API and the RabbitMQ integration events are the project's published contracts.

| Rule | Mandatory Behavior |
| --- | --- |
| **OpenAPI is the contract of record** | The generated OpenAPI document is snapshot-tested (or diffed against the last released version) on every pull request; breaking changes (`oasdiff`) fail the build unless a new API version is introduced. |
| **Spec quality** | The OpenAPI document passes Spectral linting (every operation documents success and error responses, security, and examples). |
| **Conformance** | Schemathesis (or equivalent) runs property-based tests from the OpenAPI document against the running API in CI. |
| **Event schemas** | Integration events have versioned JSON Schemas in the repository; tests assert that the serialized events validate against them and that new versions are backward-compatible. |
| **Consumer-driven contracts** | Required when a separately deployed consumer exists (Pact.NET); within the monolith, cross-context contracts are covered by integration tests and architecture tests. |
| **Third-party stubs** | Payment, carrier, and e-mail/SMS adapters are tested against recorded or provider-sandbox contracts (WireMock.Net) including failure modes: timeouts, `5xx`, malformed payloads, duplicate webhooks. |
| **CI integration** | Contract tests run on every pull request. |

| Tool | Use |
| --- | --- |
| Spectral, `oasdiff`, Schemathesis | OpenAPI linting, breaking-change detection, property-based API tests |
| `JsonSchema.Net` / `NJsonSchema` | Event schema validation |
| Pact.NET | Consumer-driven contracts (when applicable) |
| WireMock.Net | Third-party API stubs |

</ContractTestingStandards>

---

<EndToEndTestingStandards>
## 6. End-to-End Testing Standards

The repository contains no UI; E2E tests drive the **HTTP API** against the full stack (application + PostgreSQL + Valkey + RabbitMQ), run from `docker-compose-dev.yml` or a staging environment. They are kept to a minimum.

| Rule | Mandatory Behavior |
| --- | --- |
| **Critical paths only** | Registration and sign-in, browse catalog → add to cart → checkout → payment (provider sandbox) → order confirmed → stock decremented → notification queued; refund; cart/stock-reservation expiry. |
| **Asynchronous assertions** | Eventual-consistency steps are awaited with bounded polling (`Polly`/`WaitUntil` helper with timeout) — never fixed sleeps. |
| **Independent of test data** | Each test creates its own customer, catalog items, and stock through the API. |
| **Retry policy** | Controlled retries only for infrastructure flakiness; the retry rate is tracked as a health metric. |
| **Parallel execution** | Suites run in parallel without interference (unique data per test). |
| **Environment parity** | Runs against a topology mirroring production (same images, same configuration shape). |
| **Maximum execution time** | Each test ≤ 30 seconds; the full E2E suite ≤ 15 minutes. |
| **No production or real-payment use** | Payment providers are used only in sandbox/test mode with published test data. |

</EndToEndTestingStandards>

---

<PerformanceTestingStandards>
## 7. Performance and Load Testing Standards

These extend [CODE.md Section 3](./CODE.md):

| Test Type | Purpose | When to Run |
| --- | --- | --- |
| **Benchmark** | Baseline latency/throughput for critical operations (pricing, cart totals, promotion evaluation, stock reservation). | On significant changes to those paths |
| **Load test** | Behavior under expected peak traffic (e.g., campaign or sale peaks). | Before each production release |
| **Stress test** | Breaking point and degradation behavior. | Quarterly or after architecture changes |
| **Soak test** | Memory leaks, connection-pool exhaustion, queue-depth drift, outbox backlog over hours. | Before major releases |
| **Spike test** | Sudden surges (flash sales, marketing e-mails). | Before seasonal peaks |

| Rule | Mandatory Behavior |
| --- | --- |
| **Realistic data and traffic** | Production-representative catalog sizes, cart sizes, and read/write mix. |
| **Measurable SLOs** | Validate against explicit SLO targets (p50/p95/p99, throughput, error rate) from [OBSERVABILITY.md](./OBSERVABILITY.md). |
| **Regression detection** | p95/p99 regressions above 10% block release unless waived. |
| **Versioned artifacts** | Scripts, datasets, and reports are version-controlled. |
| **Isolated environment** | A dedicated environment, not shared with other suites. |
| **Resource monitoring** | Track CPU, memory, GC, thread-pool, PostgreSQL connections and locks, Valkey memory, and RabbitMQ queue depth during tests. |
| **Concurrency correctness under load** | Load tests include concurrent checkouts against limited stock and assert no overselling and no duplicate charges. |

</PerformanceTestingStandards>

---

<SecurityTestingStandards>
## 8. Security Testing Standards

These extend [SECURITY.md](./SECURITY.md). All security controls implemented in code are validated through automated tests.

| Test Type | Scope | CI Integration |
| --- | --- | --- |
| **SAST** | Code-level vulnerabilities (injection, insecure deserialization, hardcoded secrets). | Every pull request |
| **DAST** | OWASP ZAP API scan against the running API using the OpenAPI document. | Pre-release pipeline |
| **Dependency scanning** | Known CVEs in direct and transitive NuGet dependencies. | Every pull request |
| **Container scanning** | Base images and layers. | Every image build |
| **Secret scanning** | Hardcoded credentials, keys, tokens in source and history. | Pre-commit + CI |
| **Authorization tests** | Every protected endpoint rejects unauthorized access for all roles; object-level ownership is enforced. | Every pull request |
| **Input validation tests** | System boundaries reject malicious input (SQL injection, XSS payloads, oversized bodies, unknown properties). | Every pull request |
| **Cryptography tests** | Encryption, hashing, and key handling follow [SECURITY.md Section 4](./SECURITY.md) (known-answer tests, nonce uniqueness, tamper detection, Argon2id parameters, constant-time comparison usage). | Every pull request |
| **Abuse-case tests** | The abuse cases in [SECURITY.md Section 2.3](./SECURITY.md) (price tampering, coupon over-redemption, replayed webhooks, duplicate checkout) have automated tests. | Every pull request |
| **Webhook tests** | Invalid, expired, and replayed provider signatures are rejected. | Every pull request |

| Rule | Mandatory Behavior |
| --- | --- |
| **Security tests block merge** | Critical and high findings block pull requests. |
| **No security test exceptions** | Failures cannot be deferred without documented risk acceptance. |
| **Penetration test cadence** | Manual penetration testing at least annually for production systems. |
| **Regression tests for CVEs** | Every fixed vulnerability has a regression test. |

</SecurityTestingStandards>

---

<DotNetTestStack>
## 9. .NET Test Stack

### 9.1 Frameworks and Tooling

| Purpose | Recommended Tools |
| --- | --- |
| **Unit test framework** | xUnit v3 |
| **Assertions** | Shouldly or AwesomeAssertions (Apache-2.0 fork of FluentAssertions 7, same `.Should()` API) |
| **Mocking** | NSubstitute (preferred), FakeItEasy. Prefer hand-written fakes for repositories and clocks. |
| **Time** | `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) |
| **Integration tests** | `WebApplicationFactory<Program>` (`Microsoft.AspNetCore.Mvc.Testing`), Testcontainers for .NET (`Testcontainers.PostgreSql`, `Testcontainers.RabbitMq`, `Testcontainers.Redis` or a generic container with the Valkey image) |
| **Database cleanup** | Respawn |
| **Architecture tests** | NetArchTest.Rules or ArchUnitNET ([ARCHITECTURE.md Section 10.3](./ARCHITECTURE.md)) |
| **Contract tests** | Spectral, `oasdiff`, Schemathesis, `JsonSchema.Net`, WireMock.Net; Pact.NET when applicable |
| **Test data** | Hand-written builders/factories; Bogus with a fixed seed |
| **Performance tests** | BenchmarkDotNet, k6 (preferred for HTTP load), NBomber |
| **Security scanning** | .NET analyzers + SonarAnalyzer, CodeQL/Semgrep, NuGet Audit, OWASP ZAP — see [SECURITY.md Section 14](./SECURITY.md) |
| **Code coverage** | Coverlet (`coverlet.collector`) or `Microsoft.Testing.Extensions.CodeCoverage`; ReportGenerator |
| **Mutation testing** | Stryker.NET |

> **License note:** FluentAssertions 8+ is commercially licensed, and Moq's past SponsorLink incident (collection of developer e-mail hashes at build time) makes it unsuitable as a default. Do not add either to new projects.

### 9.2 Unit Test Patterns

```csharp
[Trait("Rule", "BR-ORD-001")]
public class OrderMinimumTotalTests
{
    [Fact]
    public void Should_reject_order_when_total_is_zero()
    {
        // Arrange
        var order = OrderBuilder.Draft().WithNoItems().Build();

        // Act
        var result = order.Place();

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(OrderErrors.Empty);
        order.DomainEvents.ShouldBeEmpty(); // no event on a failed operation
    }
}

public class OrderStateTransitionTests
{
    [Fact]
    public void Should_raise_OrderPlaced_when_a_valid_draft_order_is_placed()
    {
        var order = OrderBuilder.Draft().WithItem("SKU-001", quantity: 2, unitPrice: 25.00m).Build();

        var result = order.Place();

        result.IsSuccess.ShouldBeTrue();
        order.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<OrderPlaced>();
    }

    [Theory]
    [InlineData(OrderStatus.Placed)]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Cancelled)]
    public void Should_reject_adding_items_when_order_is_not_a_draft(OrderStatus status)
    {
        var order = OrderBuilder.WithStatus(status).Build();

        var result = order.AddItem(ProductId.New(), Quantity.Of(1), Money.Brl(10.00m));

        result.Error.ShouldBe(OrderErrors.NotEditable);
    }
}

public class CouponValidityTests
{
    [Fact]
    public void Should_reject_coupon_after_its_expiration_instant()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-30T23:59:59Z"));
        var coupon = CouponBuilder.ValidUntil(DateTimeOffset.Parse("2026-11-30T23:59:59Z")).Build();

        clock.Advance(TimeSpan.FromSeconds(1));

        coupon.Apply(clock.GetUtcNow()).IsFailure.ShouldBeTrue();
    }
}
```

**Key rules:**

- `[Fact]` for single cases; `[Theory]` with `[InlineData]`/`[MemberData]` for parameterized cases. Money in data rows is passed as a string or `decimal` literal — never through `double`.
- Use builders/factories for domain objects; avoid long inline constructor calls.
- Inject `TimeProvider`; use `FakeTimeProvider`; never read `DateTime.UtcNow` in code under test.
- `async Task` (never `async void`) for asynchronous tests; never `Thread.Sleep`; use `TaskCompletionSource` or bounded polling helpers.
- Do not mock what you own when a fake is simpler: prefer in-memory fakes of domain interfaces (`FakeOrderRepository`) over elaborate mock setups.

### 9.3 Integration Test Patterns

```csharp
// Shared containers for the whole test run (xUnit v3 collection fixture)
public sealed class InfrastructureFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
    public RabbitMqContainer RabbitMq { get; } = new RabbitMqBuilder().WithImage("rabbitmq:4-management-alpine").Build();
    // Valkey: new ContainerBuilder().WithImage("valkey/valkey:8-alpine").WithPortBinding(6379, true)…

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), RabbitMq.StartAsync());
        // apply EF Core migrations for every context against Postgres
    }

    public async ValueTask DisposeAsync()
    {
        await Postgres.DisposeAsync();
        await RabbitMq.DisposeAsync();
    }
}

[Collection(nameof(InfrastructureCollection))]
public class PlaceOrderApiTests(InfrastructureFixture infra) : IAsyncLifetime
{
    private readonly ApiFactory _factory = new(infra);   // WebApplicationFactory<Program> wired to the containers
    private HttpClient _client = default!;

    public async ValueTask InitializeAsync()
    {
        await infra.ResetDatabaseAsync();                // Respawn
        _client = _factory.CreateClientFor(TestUsers.Customer("ana"));
    }

    public ValueTask DisposeAsync() { _client.Dispose(); return _factory.DisposeAsync(); }

    [Fact]
    public async Task Should_return_201_and_publish_OrderPlaced_when_payload_is_valid()
    {
        var cart = await _client.GivenCartWith("SKU-001", quantity: 2);

        var response = await _client.PostAsJsonAsync("/api/v1/orders", new PlaceOrderRequest(cart.Id),
            new HttpRequestOptions { /* Idempotency-Key header */ });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        await infra.ShouldEventuallyFindOutboxMessageAsync<OrderPlaced>();
    }

    [Fact]
    public async Task Should_return_404_when_customer_requests_another_customers_order()
    {
        var order = await _factory.SeedOrderForAsync(TestUsers.Customer("bruno"));

        var response = await _client.GetAsync($"/api/v1/orders/{order.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound); // ownership enforced, no existence leak
    }

    [Fact]
    public async Task Should_return_401_when_request_is_unauthenticated()
    {
        var anonymous = _factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/v1/orders", new { });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

// Consumer idempotency: the same message twice → one side effect
[Fact]
public async Task Should_reserve_stock_once_when_PaymentAuthorized_is_delivered_twice()
{
    var message = IntegrationEvents.PaymentAuthorized(orderId, eventId: Guid.CreateVersion7());

    await consumer.HandleAsync(message);
    await consumer.HandleAsync(message);

    (await stock.ReservedQuantityAsync(sku)).ShouldBe(2);
}
```

**Key rules:**

- One container set per test run; reset data per test with Respawn; run migrations once.
- Integration tests authenticate through a test auth scheme or real JWTs signed with a test key — never by disabling authorization.
- Replace only external third-party adapters (payment, carrier, e-mail) with stubs; everything else is real.
- Async results (outbox relay, consumers) are awaited with a bounded polling helper, never `Thread.Sleep`.

### 9.4 Architecture Test Patterns

```csharp
public class DependencyDirectionTests
{
    private static readonly Assembly App = typeof(Program).Assembly;

    [Theory]
    [InlineData("Ordering")] [InlineData("Catalog")] [InlineData("Billing")] /* … every context */
    public void Domain_should_not_depend_on_infrastructure_or_frameworks(string context)
    {
        var result = Types.InAssembly(App)
            .That().ResideInNamespace($"Portfolio.{context}.Domain")
            .ShouldNot().HaveDependencyOnAny(
                $"Portfolio.{context}.Infrastructure", $"Portfolio.{context}.API",
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "RabbitMQ.Client", "StackExchange.Redis")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }
}
```

Also assert: no context references another context's internals; domain events are named in past tense and are `sealed record`s; handlers end in `Handler`; no `DateTime.UtcNow`/`DateTime.Now` outside the composition root (or via the banned-API analyzer); each `DbContext` maps only its own schema.

### 9.5 Performance Test Patterns

```csharp
[MemoryDiagnoser]
public class OrderTotalBenchmarks
{
    private readonly PricingService _pricing = new();
    private readonly Order _order = OrderBuilder.WithItems(count: 50).Build();

    [Benchmark(Baseline = true)]
    public Money CalculateTotal() => _pricing.Calculate(_order);
}
```

- BenchmarkDotNet for micro-benchmarks of critical domain operations; k6 or NBomber for HTTP load against a deployed environment.
- Benchmark results are committed for regression tracking.

</DotNetTestStack>

---

<TestDataManagement>
## 10. Test Data Management

| Rule | Mandatory Behavior |
| --- | --- |
| **No production data** | Test data never contains production data, real PII, or real payment data. |
| **Realistic domain values** | Representative business values (real-looking SKUs, BRL amounts, CPF-shaped test identifiers that are invalid in reality), not `"test123"` or `"foo"`. |
| **Factories and builders** | Builders (`OrderBuilder`, `CouponBuilder`) create valid objects with sensible defaults; tests override only what they assert on. |
| **Shared fixtures for read-only data** | Shared data only when read-only. |
| **Deterministic generation** | Randomly generated data uses a fixed seed (Bogus `Randomizer.Seed`). |
| **Test data isolation** | Each test owns its data lifecycle. |
| **No reliance on database seeds** | No dependence on global seed scripts. |
| **Payment sandbox data** | Only the payment provider's published test cards/tokens. |

</TestDataManagement>

---

<FlakyTestPolicy>
## 11. Flaky Test Policy

A flaky test produces inconsistent results across runs without code changes.

| Rule | Mandatory Behavior |
| --- | --- |
| **Immediate quarantine** | A flaky test is moved to a quarantine suite within 24 hours (trait `[Trait("Quarantine", "true")]`, excluded from the blocking run but still executed and reported). |
| **Quarantine visibility** | Tracked in the issue tracker with an owner and a fix deadline. |
| **Fix deadline** | Fixed or permanently removed within 2 sprints (or 4 weeks). |
| **Root cause analysis** | Documented before restoring to the main suite. |
| **Tolerated rate** | Overall flaky rate below 1% ([CODE.md Section 2.1](./CODE.md)). |
| **CI flaky detection** | Failing tests are re-run automatically and inconsistencies are flagged; re-runs never hide a failure. |
| **No normalization** | "It's always been flaky" is not acceptable. |

| Root Cause | Prevention |
| --- | --- |
| **Shared mutable state** | Each test owns its data lifecycle; no global mutable state. |
| **Time dependency** | Inject `TimeProvider`; never read `DateTime.Now`/`UtcNow` in code under test. |
| **Race conditions / async pipelines** | Bounded polling with a timeout, `TaskCompletionSource`, or explicit synchronization; never `Thread.Sleep`/`Task.Delay` as a wait. |
| **Port conflicts** | Dynamic ports for containers and test servers. |
| **Container readiness** | Use Testcontainers wait strategies; do not assume a container is ready after start. |
| **External service calls** | Stub third-party APIs; never call live providers. |
| **Ordering dependency** | Tests are order-independent; no suite-level setup with side effects beyond the shared container fixture. |

</FlakyTestPolicy>

---

<CoverageAndQualityGates>
## 12. Coverage and Quality Gates

Thresholds align with [CODE.md Section 2.1](./CODE.md):

| Metric | Threshold | Scope |
| --- | --- | --- |
| **Line coverage** | ≥ 80% | Domain and Application code |
| **Branch coverage** | ≥ 70% | Decision-heavy modules (state machines, validation, calculations) |
| **Mutation score** | ≥ 60% (recommended) | Core domain (Ordering, Billing, Inventory, Promotions, Checkout) |
| **Flaky test rate** | < 1% | Entire suite |

| Rule | Mandatory Behavior |
| --- | --- |
| **Coverage enforcement in CI** | Thresholds are enforced; drops below threshold block merge. |
| **No coverage gaming** | Tests written only to raise coverage are a defect. |
| **Coverage exclusions** | Generated code, EF migrations, contract records without logic, and composition-root wiring may be excluded with documented justification (`[ExcludeFromCodeCoverage]` with reason or a coverage config). |
| **Incremental coverage** | New code meets or exceeds the threshold; pull requests that reduce coverage are blocked. |

Tooling: Coverlet (`dotnet test --collect:"XPlat Code Coverage"`) with ReportGenerator; Stryker.NET for mutation testing on the core domain.

</CoverageAndQualityGates>

---

<CICDTestPipeline>
## 13. CI/CD Test Pipeline Integration

| Stage | Tests Executed | Trigger | Blocking |
| --- | --- | --- | --- |
| **Pre-commit (local)** | Formatting (CSharpier), analyzers, fast unit tests, secret scan | Developer commit | Advisory |
| **Pull request** | Unit, architecture, integration (Testcontainers), contract tests; SAST; dependency and secret scanning | PR open/update | Yes |
| **Pre-merge** | Full suite including slow integration tests; coverage enforcement | Merge queue entry | Yes |
| **Pre-release** | Load tests, E2E flows, DAST (ZAP), image scan, migration-from-previous-schema test | Release candidate tag | Yes |
| **Post-deploy (smoke)** | Smoke tests, health checks, critical-path E2E (read-mostly) | After deployment | Rollback |

| Rule | Mandatory Behavior |
| --- | --- |
| **Test parallelization** | Independent suites run in parallel in CI. |
| **Caching** | NuGet and build caches are used; container images are cached between runs. |
| **Failure notification** | Failures notify the responsible owner with actionable diagnostics. |
| **Artifact retention** | Test reports, coverage reports, and performance results are stored as pipeline artifacts. |
| **Maximum pipeline duration** | The pull-request pipeline (unit + architecture + integration + contract) completes within 15 minutes. |
| **Runner requirements** | CI runners provide Docker for Testcontainers; workflows live in `.github/workflows/`. |

</CICDTestPipeline>

---

<DefinitionOfDone>
## 14. Testing Definition of Done

A delivery is complete from a testing perspective only when all items below are true:

1. All new business logic has unit tests covering valid, invalid, boundary, and regression scenarios, tagged with the business rule ID.
2. All integration boundaries (PostgreSQL, Valkey, RabbitMQ, HTTP) have integration tests using isolated Testcontainers.
3. Retriable handlers and consumers have explicit idempotency tests.
4. Architecture tests pass and cover any new context, layer, or dependency.
5. Contract tests verify OpenAPI and event-schema compatibility.
6. Security controls and abuse cases are validated by automated tests aligned with [SECURITY.md](./SECURITY.md), including authorization and object ownership per endpoint.
7. Performance-critical paths have benchmarks or load tests with documented SLO validation.
8. Coverage thresholds are met: ≥ 80% line, ≥ 70% branch.
9. No flaky tests in the main suite; quarantined tests have owners and deadlines.
10. Test names use domain language and follow [Section 3.3](#33-test-naming-and-structure).
11. Test data is realistic, deterministic, and free of production data and PII.
12. All tests pass in CI with no manual intervention.
13. E2E flows cover the critical business flows affected by the change.
14. Database tests validate migrations, constraints, traceability fields, soft delete, concurrency, and outbox/inbox behavior.
15. Test artifacts (reports, coverage, performance results) are published as pipeline artifacts.

Any exception to these standards must be documented with owner, scope, risk, rationale, and expiration date.

</DefinitionOfDone>

---

_Last updated: September 30, 2026_
_These standards must be reviewed and updated at minimum once per year or after any significant testing incident._

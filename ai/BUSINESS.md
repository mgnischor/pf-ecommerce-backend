# Business Rules Standards

> **Scope:** These standards apply to the `pf-ecommerce-backend` repository (C# / .NET 10 modular monolith, DDD by bounded context). Every item marked as mandatory is a hard requirement unless a documented exception is approved by the maintainers. Business rules are the core expression of domain logic and are first-class engineering artifacts — explicit, testable, traceable, and independent of infrastructure concerns.

---

## Table of Contents

1. [General Principles](#1-general-principles)
2. [Business Rule Classification](#2-business-rule-classification)
3. [Rule Definition and Documentation](#3-rule-definition-and-documentation)
4. [Domain Modeling](#4-domain-modeling)
    - 4.1 [Entities, Value Objects, and Aggregates](#41-entities-value-objects-and-aggregates)
    - 4.2 [Domain Events](#42-domain-events)
    - 4.3 [Domain Services](#43-domain-services)
5. [Validation and Invariant Enforcement](#5-validation-and-invariant-enforcement)
6. [State Machines and Workflow Rules](#6-state-machines-and-workflow-rules)
7. [Calculation and Derivation Rules](#7-calculation-and-derivation-rules)
8. [Authorization and Access Rules](#8-authorization-and-access-rules)
9. [Time, Scheduling, and Temporal Rules](#9-time-scheduling-and-temporal-rules)
10. [Integration and External System Rules](#10-integration-and-external-system-rules)
11. [Feature Flags and Conditional Rules](#11-feature-flags-and-conditional-rules)
12. [Testing Business Rules](#12-testing-business-rules)
13. [Business Rule Change Management](#13-business-rule-change-management)
14. [Recommended Libraries](#14-recommended-libraries)
15. [Business Rules Definition of Done](#15-business-rules-definition-of-done)

---

<GeneralPrinciples>
## 1. General Principles

| Principle | Mandatory Behavior |
| --- | --- |
| **Domain First** | Business rules are expressed in domain terms, not infrastructure or framework concepts. |
| **Explicit Over Implicit** | Every business rule is explicitly defined and traceable. Hidden logic in controllers or infrastructure is forbidden. |
| **Single Source of Truth** | Each business rule has exactly one authoritative implementation. Duplication across layers is a defect. |
| **Testability by Design** | Every business rule is independently testable without a database, network, or file system. |
| **Traceability** | Every business rule is traceable to a documented requirement, user story, or business decision. |
| **Isolation from Infrastructure** | Core domain logic does not depend on EF Core, ASP.NET Core, RabbitMQ, or Valkey. |
| **Fail Deterministically** | A violated business rule produces a clear, domain-meaningful error — never a silent skip. |
| **Auditability** | Changes to business-critical state (orders, payments, stock, prices) are auditable via domain events, audit logs, or traceability fields. |

</GeneralPrinciples>

---

<BusinessRuleClassification>
## 2. Business Rule Classification

Every business rule is classified by type. The classification drives implementation strategy, testing approach, and change management.

| Type | Description | E-commerce example |
| --- | --- | --- |
| **Constraint** | A condition that must hold for domain state to be valid. | An order total must be greater than zero. |
| **Derivation** | A rule that computes a value from other data. | Order total is derived from line items, promotions, shipping, and taxes. |
| **Invariant** | A condition that holds across the entire lifecycle of an aggregate. | Available stock must never go negative. |
| **Authorization** | A rule that determines who can perform an action. | A customer can only view their own orders; only staff can issue refunds. |
| **Temporal** | A rule that depends on time or deadlines. | A promotional price is valid only within its date range; a cart expires after inactivity. |
| **State Transition** | A rule governing allowed state changes. | An order can move from `Placed` to `Paid`, never back to `Draft`. |
| **Workflow/Orchestration** | A rule coordinating multi-step processes. | Checkout must reserve stock before charging the payment. |
| **Notification/Trigger** | A rule firing a side effect when a condition is met. | Send a confirmation e-mail when an order is paid. |
| **Configuration** | A rule whose parameters are configurable without code changes. | Maximum quantity per item per order; cart expiration window. |

### 2.1 Classification Requirements

- Every business rule implemented in code carries a classification annotation, comment, or metadata tag linking it to one of the types above and to its `BR-` ID.
- Misclassified rules are corrected during code review.
- Rules spanning multiple types are decomposed into discrete, single-type rules wherever possible.

</BusinessRuleClassification>

---

<RuleDefinitionAndDocumentation>
## 3. Rule Definition and Documentation

### 3.1 Mandatory Rule Attributes

| Attribute | Description |
| --- | --- |
| **Rule ID** | A unique, stable identifier (e.g., `BR-ORD-001`). |
| **Name** | A concise, domain-meaningful name. |
| **Classification** | The rule type from [Section 2](#2-business-rule-classification). |
| **Description** | A clear, unambiguous natural-language statement. |
| **Preconditions** | Conditions that must be true before the rule applies. |
| **Postconditions** | The expected outcome or state change. |
| **Error Behavior** | What happens on violation (error code, rejection, fallback). |
| **Owner** | The person or team responsible for the rule's correctness. |
| **Source** | Link to the requirement, user story, or business decision. |
| **Effective Date** | When the rule becomes or became active. |
| **Criticality** | Whether the rule is financial, compliance-sensitive, or standard — drives the review rigor in [Section 13.3](#133-audit-and-compliance). |

### 3.2 Documentation Location

- Rules are documented in a rules catalog under `docs/business-rules/`, one file per bounded context (e.g., `docs/business-rules/ordering.md`).
- Rules are version-controlled alongside the code that implements them.

### 3.3 Naming Conventions

- Rule identifiers use `BR-{CONTEXT}-{SEQUENCE}` with these context codes: `CAT` (Catalog), `CRT` (Cart), `CHK` (Checkout), `ORD` (Ordering), `BIL` (Billing), `INV` (Inventory), `SHP` (Shipping), `PRM` (Promotions), `REV` (Reviews), `CUS` (Customers), `IDN` (Identity), `NTF` (Notifications).
- Rule names use business language, not technical jargon: "Order Minimum Total Constraint", not "Validation Rule 1".

</RuleDefinitionAndDocumentation>

---

<DomainModeling>
## 4. Domain Modeling

### 4.1 Entities, Value Objects, and Aggregates

| Concept | Mandatory Behavior |
| --- | --- |
| **Entity** | Has a unique identity. Rules that depend on identity belong on the entity. |
| **Value Object** | Immutable and compared by value (`Money`, `Sku`, `Quantity`, `Address`, `Email`). |
| **Aggregate** | Enforces all invariants within its boundary. External code interacts through the aggregate root only. |

Rules:

- Keep aggregates small — only what is needed to enforce invariants.
- Cross-aggregate references use identifiers, not object references.
- Never let external code modify aggregate internals; all mutations go through aggregate methods that enforce business rules.
- Aggregate boundaries are documented and reviewed during design.

### 4.2 Domain Events

| Requirement | Mandatory Behavior |
| --- | --- |
| **Event Naming** | Past-tense, domain-meaningful (`OrderPlaced`, `PaymentFailed`, `StockReserved`). |
| **Event Immutability** | Immutable once created. |
| **Event Content** | Carries enough data for consumers to act without querying back to the source. |
| **Event Versioning** | Events published outside the context follow an explicit versioning and compatibility strategy. |
| **Event–Rule Traceability** | Every event is traceable to the business rule or state transition that triggered it. |
| **Event Metadata** | Every event carries a unique event ID, the originating aggregate ID, and a UTC timestamp. Events published outside the context additionally carry correlation and causation IDs. |

### 4.3 Domain Services

- Used only for business logic that does not naturally belong to a single entity or value object.
- Stateless.
- No infrastructure dependencies; inject abstractions for anything external.
- No catch-all "managers" — each focuses on a single business capability.

</DomainModeling>

---

<ValidationAndInvariantEnforcement>
## 5. Validation and Invariant Enforcement

### 5.1 Validation Layers

| Layer | Responsibility |
| --- | --- |
| **API Boundary** | Validate format, type, range, and required fields on all external input. |
| **Domain Layer** | Enforce business invariants, constraints, and cross-field rules within entities and aggregates. |
| **Persistence Layer** | Database constraints (`NOT NULL`, `UNIQUE`, `CHECK`, `FOREIGN KEY`) as a safety net, not primary logic. |

### 5.2 Mandatory Validation Rules

- All input validation happens before any business logic executes.
- Domain invariants are enforced inside the domain model, not in controllers, services, or middleware.
- Validation errors return structured, domain-meaningful messages — never stack traces or internal identifiers.
- Composite validations (multi-field, cross-entity) are explicit rule objects or methods, not scattered code.
- Domain methods return a `Result` (or throw a dedicated `DomainException` for truly exceptional states) instead of signalling expected business failures with generic exceptions.

### 5.3 Validation Error Structure

The HTTP API returns errors as **Problem Details (RFC 9457)** (JSON properties are `camelCase` — [API_CONTRACTS.md Section 3](./API_CONTRACTS.md#3-request-and-response-conventions); the full contract is in [API_CONTRACTS.md Section 4](./API_CONTRACTS.md#4-validation-and-error-contract)) with media type `application/problem+json` (`type`, `title`, `status`, `detail`, `instance`), implemented with ASP.NET Core `IProblemDetailsService`/`ProblemDetails`. Business-rule and validation failures extend that document with an `errors` array in which every entry includes at minimum:

| Field | Description |
| --- | --- |
| `ruleId` | The business rule that was violated. |
| `field` | The field or property that failed validation. |
| `message` | A human-readable error message. |
| `code` | A machine-readable error code for client-side handling. |

```json
{
    "type": "https://errors.example.com/business-rule-violation",
    "title": "The order violates a business rule.",
    "status": 422,
    "instance": "/api/v1/orders/7f3c9a2e",
    "errors": [
        {
            "ruleId": "BR-ORD-001",
            "field": "total",
            "code": "ORDER_TOTAL_BELOW_MINIMUM",
            "message": "The order total must be at least 10.00 BRL."
        }
    ]
}
```

Status mapping: malformed/invalid input → `400`/`422`; unauthenticated → `401`; forbidden → `403`; not found → `404`; concurrency or state-transition conflict → `409`; idempotency-key reuse with a different payload → `422`.

Integration events on RabbitMQ that report a rule failure carry the same four fields in the message body.

</ValidationAndInvariantEnforcement>

---

<StateMachinesAndWorkflowRules>
## 6. State Machines and Workflow Rules

### 6.1 State Machine Requirements

Any entity with a lifecycle (Order, Payment, Shipment, Stock Reservation, Cart, Refund) defines its state transitions explicitly:

| Requirement | Mandatory Behavior |
| --- | --- |
| **Explicit State Diagram** | Every stateful entity has a documented state diagram showing all valid transitions. |
| **Transition Guards** | Every transition has an explicit guard validating whether it is allowed. |
| **Rejected Transitions** | Invalid transitions produce clear domain errors, never silent failures. |
| **Transition Events** | Every transition emits a domain event recording the transition, actor, and timestamp. |
| **State-Driven Behavior** | Behavior is driven by the current state; avoid conditionals that check multiple states. |
| **Concurrent Transition Safety** | When two actors may transition the same entity concurrently, the guard relies on the aggregate's optimistic concurrency control ([ARCHITECTURE.md](./ARCHITECTURE.md) Section 4.1) so only one succeeds and the loser receives a `409 Conflict`. |

### 6.2 Workflow Orchestration Rules

- Multi-step flows that span aggregates or contexts (e.g., checkout → reserve stock → authorize payment → place order → schedule shipment) are implemented as an explicit saga / process manager driven by domain and integration events.
- Each step is idempotent so it can be retried safely.
- Compensation is defined for each step that can fail after producing side effects (e.g., release a stock reservation if payment authorization fails).
- Workflow state is persisted in PostgreSQL — in-memory-only state is not acceptable for business-critical processes.
- Timeout and escalation policies are defined for every workflow involving external dependencies (payment, carrier) or expiring holds (stock reservation).

</StateMachinesAndWorkflowRules>

---

<CalculationAndDerivationRules>
## 7. Calculation and Derivation Rules

### 7.1 General Requirements

| Requirement | Mandatory Behavior |
| --- | --- |
| **Determinism** | The same inputs always produce the same output. |
| **Precision** | Money and monetary calculations use `decimal` (PostgreSQL `NUMERIC`) — `float`/`double` are forbidden. |
| **Rounding Policy** | Every calculation that rounds declares its mode explicitly (`MidpointRounding.ToEven` or `AwayFromZero`) and scale; the policy is centralized. |
| **Unit Consistency** | Currency (and weight, dimensions) are enforced at the type level: adding `Money` in different currencies is rejected. |
| **Price Snapshot** | An order line stores the unit price, discounts, taxes, and currency that applied at purchase time. Later catalog or promotion changes never alter historical orders. |
| **Audit Trail** | Derived values affecting billing or reporting record their inputs and formula version. |
| **Currency Conversion** | Any conversion records the exchange rate, its source, and capture timestamp, so the converted amount remains reproducible. |

### 7.2 Formula Management

- Business formulas (price, discount stacking, tax, shipping cost, refund amount) are centralized in domain services or value objects — not scattered across controllers, queries, and consumers.
- When a formula changes, a new version is created; historical data remains associated with the formula version used.
- Formula changes that impact financial outputs undergo formal review before deployment.

</CalculationAndDerivationRules>

---

<AuthorizationAndAccessRules>
## 8. Authorization and Access Rules

> This section addresses **business-level access control** — who can perform which business actions and under what conditions. Technical authentication and infrastructure controls are in [SECURITY.md](./SECURITY.md).

### 8.1 Business Authorization Requirements

| Requirement | Mandatory Behavior |
| --- | --- |
| **Role-Based Access (RBAC)** | Roles align with business functions (e.g., `Customer`, `CatalogManager`, `SupportAgent`, `FinanceAnalyst`, `Administrator`). |
| **Attribute-Based Access (ABAC)** | Attribute conditions supplement RBAC where needed — most importantly **resource ownership** (a customer sees only their own cart, orders, addresses, reviews). |
| **Least Privilege** | Users and service accounts have only the permissions needed for their current function. |
| **Separation of Duties** | Critical operations (e.g., approve and execute a large refund) require distinct actors. |
| **Delegation Rules** | Delegation of authority is explicit, time-bounded, and auditable. |
| **Default Deny** | Any role/action/resource combination not explicitly in the authorization matrix is denied. |

### 8.2 Data Visibility Rules

- Ownership checks are enforced in the Application/Domain layer (and, where suitable, as EF Core global query filters), never only in the API layer.
- Sensitive fields (PII, payment data) have explicit access rules documented and enforced.
- Object-level authorization (BOLA/IDOR) is tested for every endpoint that takes a resource ID ([SECURITY.md](./SECURITY.md)).

### 8.3 Action Authorization Matrix

The project maintains an authorization matrix (in `docs/business-rules/authorization-matrix.md`) mapping:

| Dimension | Description |
| --- | --- |
| **Role/Actor** | Who performs the action. |
| **Action** | The business operation. |
| **Resource** | The entity or data the action applies to. |
| **Conditions** | Additional constraints (ownership, status, time). |
| **Decision** | Allow, deny, or require escalation. |

The matrix is reviewed and updated whenever roles, features, or business processes change.

</AuthorizationAndAccessRules>

---

<TemporalRules>
## 9. Time, Scheduling, and Temporal Rules

### 9.1 General Temporal Requirements

| Requirement | Mandatory Behavior |
| --- | --- |
| **UTC by Default** | All timestamps stored and processed internally are UTC (`DateTimeOffset` with zero offset / PostgreSQL `timestamptz`). Localize only in the presentation layer. |
| **Timezone Awareness** | Rules depending on local time (cutoff times, campaign start at local midnight) explicitly declare a timezone (IANA ID). |
| **Effective Dating** | Rules that change over time (prices, promotions, tax tables) use effective-date ranges, not deployments. |
| **Expiration Behavior** | Anything with a deadline defines explicit grace-period and expiration behavior (cart expiry, stock-reservation TTL, payment-authorization expiry, coupon validity). |
| **Clock Abstraction** | Domain and Application code use an injected `TimeProvider`; never `DateTime.Now`/`UtcNow`. |
| **Date vs. Datetime** | A calendar date (birth date, estimated delivery date) is modeled and stored as `DateOnly`/`date`, not a timestamp. |

### 9.2 Scheduling Rules

- Scheduled jobs (reservation expiry, abandoned-cart cleanup, outbox relay) are idempotent and safe under concurrent execution across multiple application instances (e.g., via PostgreSQL advisory locks or `SELECT ... FOR UPDATE SKIP LOCKED`).
- Scheduling configuration (intervals, cron expressions) is externalized.
- Every job has an explicit timeout, a failure-handling strategy, and an alert ([OBSERVABILITY.md](./OBSERVABILITY.md)).
- Time-window rules account for daylight-saving transitions.

</TemporalRules>

---

<IntegrationRules>
## 10. Integration and External System Rules

### 10.1 General Integration Requirements

| Requirement | Mandatory Behavior |
| --- | --- |
| **Contract-First Design** | Integrations are defined by explicit contracts (OpenAPI, JSON Schema, event definitions). |
| **Idempotency** | Operations with side effects in external systems (charge, refund, create shipment, send e-mail) are idempotent — send an idempotency key to the provider. |
| **Retry and Circuit Breaking** | Every external call has a retry policy and circuit-breaker threshold (Polly / `Microsoft.Extensions.Http.Resilience`). |
| **Timeout Policy** | Every external call has an explicit timeout. No unbounded waits. |
| **Data Mapping Isolation** | External/internal model translation happens in a dedicated anti-corruption layer (`Infrastructure/ExternalServices`). |
| **Fallback Behavior** | Every integration defines what happens when the external system is unavailable (degrade, queue via outbox, reject). |

### 10.2 Business Data Exchange Rules

- Data from external systems is validated against business rules before entering the domain. Payment-provider webhooks are authenticated (signature verification) and deduplicated by provider event ID.
- Outbound data is filtered to what the consumer is authorized to receive.
- External identifiers (payment provider IDs, carrier tracking numbers) are never internal primary keys — store them as attributes with a mapping.
- All integration flows are logged (without secrets or PII) for traceability and reconciliation.

### 10.3 SLA and Dependency Management

- Every external dependency has a documented SLA expectation (availability, latency).
- Business rules do not assume 100% availability of any external system.
- When a dependency violates its SLA, the system degrades according to predefined business rules.

</IntegrationRules>

---

<FeatureFlagsAndConditionalRules>
## 11. Feature Flags and Conditional Rules

### 11.1 Feature Flag Requirements

| Requirement | Mandatory Behavior |
| --- | --- |
| **Explicit Lifecycle** | Every flag has an owner, purpose, and planned removal date. |
| **Clean Separation** | Flagged code paths are cleanly separated — avoid deeply nested conditionals. |
| **Default-Off for Risk** | New or risky rules default to off and are enabled per environment. |
| **Audit Trail** | Every flag state change is logged with actor, timestamp, and reason. |
| **Testing Both Paths** | Both the enabled and disabled paths are covered by automated tests. |

### 11.2 Conditional Business Rules

- Rules that vary by customer tier, region, or campaign are implemented via the strategy pattern or externalized configuration — not hardcoded `if/else` chains.
- Conditional rule parameters live in configuration or the database, not in source code.
- When a conditional rule is retired, its code, configuration, and tests are removed in the same change.

</FeatureFlagsAndConditionalRules>

---

<TestingBusinessRules>
## 12. Testing Business Rules

> These requirements extend [TESTS.md](./TESTS.md) (in particular Section 3.2) with domain-specific obligations.

### 12.1 Mandatory Test Coverage

| Test Type | Requirement |
| --- | --- |
| **Unit Tests** | Every business rule has dedicated unit tests covering valid, invalid, and boundary conditions. |
| **Invariant Tests** | Aggregate invariants are tested with scenarios that attempt to violate them. |
| **State Transition Tests** | Every valid and invalid state transition has an explicit test case. |
| **Calculation Tests** | Derivations are tested with known-answer tests and edge cases (zero, maximum, rounding boundaries, currency mismatch). |
| **Authorization Tests** | Business-level access rules are tested for permitted and denied scenarios per role, including ownership. |
| **Temporal Tests** | Time-dependent rules are tested with a fake `TimeProvider` (`FakeTimeProvider`) covering boundaries. |
| **Integration Tests** | Cross-context and external-system rules are validated in integration tests with contract verification. |

### 12.2 Test Quality Requirements

- Test names use domain language (`should_reject_order_when_total_is_below_minimum`).
- Tests do not depend on infrastructure unless explicitly testing integration.
- Test data represents real business scenarios — avoid meaningless dummy data.
- A regression test is added for every business rule bug found in production.

### 12.3 Security Test Alignment

- Rules that enforce access control, data visibility, or sensitive operations are also validated against [SECURITY.md](./SECURITY.md).

</TestingBusinessRules>

---

<BusinessRuleChangeManagement>
## 13. Business Rule Change Management

### 13.1 Change Process

| Step | Mandatory Action |
| --- | --- |
| **Impact Analysis** | Before changing a rule, assess impact on dependent rules, workflows, integrations, and stored data. |
| **Version Control** | Commit messages reference the rule ID (`BR-ORD-001`). |
| **Backward Compatibility** | Rule changes remain backward-compatible during transition windows unless a breaking change is approved. |
| **Stakeholder Review** | Changes to financial rules (pricing, discounts, tax, refunds) are reviewed by the rule owner, not only the implementer. |
| **Rollback Plan** | Every rule change has a rollback strategy. |

### 13.2 Deprecation Policy

- Deprecated rules carry a deprecation notice, replacement reference, and removal date, and keep working until removal.
- Removal includes cleanup of code, configuration, tests, and documentation.

### 13.3 Audit and Compliance

- Changes to rules affecting financial or legal behavior are recorded in an audit log.
- Audit records include who changed the rule, when, what changed, and the justification.
- Compliance-sensitive rules include evidence of review in the change history.

</BusinessRuleChangeManagement>

---

<RecommendedLibraries>
## 14. Recommended Libraries

Libraries belong in the Domain layer only when pure (no I/O): money, decimal, date/time, and result types. Everything else (feature-flag providers, workflow engines, policy engines) is consumed through a Domain- or Application-defined interface and implemented in Infrastructure, per [ARCHITECTURE.md Section 6](./ARCHITECTURE.md).

| Requirement | Library / approach |
| --- | --- |
| **Exact decimal arithmetic (§7.1)** | `decimal` (built-in); PostgreSQL `NUMERIC` |
| **Money and currency (§7.1)** | Own `Money` value object over `decimal` with ISO 4217 currency code; NodaMoney is an acceptable alternative |
| **Dates, time zones, clock (§9)** | `DateOnly`, `DateTimeOffset`, `TimeProvider` (`FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` in tests); NodaTime only for complex calendar/time-zone rules |
| **Validation (§5)** | FluentValidation for API input; domain guards in constructors and factory methods |
| **Result type (§5.2)** | Own `Result`/`Result<T>` in `SharedKernel` (or `ErrorOr`/`FluentResults` if approved) |
| **State machines (§6.1)** | Explicit transition methods on the aggregate with exhaustive `switch` expressions; `Stateless` only for complex flows |
| **Workflow orchestration (§6.2)** | Saga/process manager built on the chosen messaging library ([ARCHITECTURE.md Section 9.4](./ARCHITECTURE.md)); Temporal .NET SDK only with an ADR |
| **Feature flags (§11)** | OpenFeature .NET SDK + provider (or `Microsoft.FeatureManagement` with an ADR); code depends only on an abstraction |
| **Authorization (§8)** | ASP.NET Core authorization policies and handlers (resource-based for ownership) |
| **Configurable rule engines (§11.2)** | Prefer the strategy pattern. Introduce an engine (e.g., Microsoft `RulesEngine`) only when business users author rules, via an ADR |

</RecommendedLibraries>

---

<DefinitionOfDone>
## 15. Business Rules Definition of Done

A delivery that impacts business rules is complete only when all items below are true:

1. The rule is documented with all mandatory attributes from [Section 3](#3-rule-definition-and-documentation).
2. The rule is classified per [Section 2](#2-business-rule-classification).
3. The rule is implemented in the Domain layer, isolated from infrastructure.
4. Domain invariants are enforced within aggregates and cannot be bypassed by external code.
5. State transitions are explicitly defined, guarded, and emit domain events.
6. Calculations use appropriate precision, rounding, and currency handling.
7. Authorization rules (including ownership) are enforced below the API layer, not only in controllers.
8. Temporal rules use an injected `TimeProvider` and explicit timezone handling.
9. All business rule tests pass, covering valid, invalid, boundary, and regression scenarios.
10. Integration contracts are validated and external-system fallback behavior is defined.
11. Feature flags (if used) have a defined lifecycle and both paths are tested.
12. The change was reviewed by the developer and the rule owner.
13. Security requirements from [SECURITY.md](./SECURITY.md) are respected for access control and sensitive data.
14. A rollback strategy is documented for the change.

Any exception to these standards must be documented with owner, scope, risk, rationale, and expiration date.

</DefinitionOfDone>

---

_Last updated: September 30, 2026_
_These standards must be reviewed and updated at minimum once per year or after any significant business rules incident._

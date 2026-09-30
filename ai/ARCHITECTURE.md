# Architecture Standards

> **Scope:** These standards apply to the `pf-ecommerce-backend` repository: a **modular monolith** written in **C# / .NET 10**, organized with **Domain-Driven Design (DDD)** by bounded context. Persistence is **PostgreSQL** (Npgsql + Entity Framework Core), caching is **Valkey**, messaging is **RabbitMQ**, telemetry is **OpenTelemetry**, and the HTTP API is documented with **OpenAPI + Scalar**. Every item marked as mandatory is a hard requirement unless a documented exception is approved by the maintainers. Architecture decisions must prioritize modularity, domain alignment, evolvability, and clarity.

---

## Table of Contents

1. [General Principles](#1-general-principles)
2. [Domain-Driven Design as Structural Foundation](#2-domain-driven-design-as-structural-foundation)
    - 2.1 [Strategic Design](#21-strategic-design)
    - 2.2 [Tactical Design](#22-tactical-design)
    - 2.3 [Folder and File Organization by Domain](#23-folder-and-file-organization-by-domain)
3. [SOLID as Architectural Discipline](#3-solid-as-architectural-discipline)
4. [Rich Domain Modeling](#4-rich-domain-modeling)
    - 4.1 [Aggregate Design Rules](#41-aggregate-design-rules)
    - 4.2 [Entity and Value Object Rules](#42-entity-and-value-object-rules)
    - 4.3 [Domain Services](#43-domain-services)
    - 4.4 [Domain Events](#44-domain-events)
    - 4.5 [Anti-Patterns to Avoid](#45-anti-patterns-to-avoid)
5. [Modularity as a Base Principle](#5-modularity-as-a-base-principle)
    - 5.1 [Module Boundaries](#51-module-boundaries)
    - 5.2 [Module Communication](#52-module-communication)
    - 5.3 [Module Independence](#53-module-independence)
    - 5.4 [Modular Monolith as Default](#54-modular-monolith-as-default)
6. [Layered Architecture](#6-layered-architecture)
    - 6.1 [Mandatory Layers](#61-mandatory-layers)
    - 6.2 [Dependency Direction](#62-dependency-direction)
    - 6.3 [Layer Responsibility Matrix](#63-layer-responsibility-matrix)
7. [API and Contract Design](#7-api-and-contract-design)
8. [Cross-Cutting Concerns](#8-cross-cutting-concerns)
9. [Infrastructure and External Dependencies](#9-infrastructure-and-external-dependencies)
10. [Evolvability and Migration Strategy](#10-evolvability-and-migration-strategy)
11. [Architecture Decision Records](#11-architecture-decision-records)
12. [Architecture Definition of Done](#12-architecture-definition-of-done)

---

<GeneralPrinciples>
## 1. General Principles

All architectural decisions must follow the same non-negotiable principles:

| Principle | Mandatory Behavior |
| --- | --- |
| **Domain Alignment** | The codebase structure must mirror business domains, not technical layers or frameworks. |
| **Modularity First** | The system is composed of cohesive, loosely coupled bounded-context modules with explicit boundaries. |
| **SOLID by Default** | SOLID principles are mandatory at all levels — from class design to module boundaries — as defined in [CODE.md](./CODE.md). |
| **Explicit Boundaries** | Every architectural boundary (module, layer, context) must be explicitly defined and enforced by automated tests, never implied. |
| **Dependency Inversion Everywhere** | High-level domain logic must never depend on infrastructure details. All cross-boundary dependencies flow through abstractions. |
| **Evolvability Over Perfection** | Architecture must support incremental evolution. Avoid big-bang redesigns; prefer small, safe structural changes. |
| **Observability by Design** | Every module must expose logs, metrics, and traces sufficient for production diagnosis. See [OBSERVABILITY.md](./OBSERVABILITY.md). |
| **Security as Architecture** | Security controls must be part of the architectural design, not bolted on after implementation. See [SECURITY.md](./SECURITY.md). |
| **Testability by Design** | Domain and Application layers must be verifiable in isolation, without a database, network, or external framework. Infrastructure dependencies must be substitutable through the interfaces defined in Domain/Application. |
| **Convention Over Configuration** | Consistent conventions across bounded contexts reduce cognitive load and onboarding time. |
| **Documentation as Code** | Architecture decisions, boundaries, and contracts are version-controlled alongside the codebase. |

</GeneralPrinciples>

---

<DomainDrivenDesign>
## 2. Domain-Driven Design as Structural Foundation

DDD is the mandatory approach for organizing the codebase. The structure of files, folders, and namespaces must reflect business domains — never technical roles.

### 2.1 Strategic Design

| Concept | Mandatory Behavior |
| --- | --- |
| **Bounded Context** | Every distinct business domain is implemented within its own Bounded Context with explicit boundaries. |
| **Ubiquitous Language** | Each Bounded Context defines and uses a consistent domain vocabulary. The same term must not have different meanings across contexts (e.g., a `Product` in Catalog is not a `Product` in Inventory). |
| **Context Mapping** | Relationships between Bounded Contexts are documented in a Context Map. |
| **Subdomain Alignment** | Core, Supporting, and Generic subdomains are identified in an ADR. Core subdomains receive the highest investment in modeling quality and test coverage. |

#### Bounded Contexts

| Context | Business capability |
| --- | --- |
| **Catalog** | Product discovery: products, categories, attributes, search. |
| **Cart** | Shopper's pre-purchase item selection. |
| **Checkout** | Conversion of a cart into a confirmed purchase intent (addresses, shipping choice, payment choice). |
| **Ordering** | Order lifecycle after checkout. |
| **Billing** | Payments, invoices, refunds. |
| **Inventory** | Stock levels and reservations. |
| **Shipping** | Fulfillment, carriers, tracking. |
| **Promotions** | Discounts, coupons, campaigns. |
| **Reviews** | Customer ratings and reviews. |
| **Customers** | Customer profiles and addresses. |
| **Identity** | Authentication, credentials, roles, and permissions. |
| **Notifications** | Outbound e-mail/SMS/push messages triggered by domain events. |
| **SharedKernel** | Minimal shared primitives (see [Section 2.3.3](#233-mandatory-folder-rules)). |

#### Context Map Requirements

- The project maintains a Context Map (diagram or table) under `docs/` showing all Bounded Contexts and their integration relationships, updated whenever contexts are added, removed, or their relationships change.
- Relationship patterns must be explicitly documented:

| Pattern | When to Use |
| --- | --- |
| **Shared Kernel** | Contexts co-own a small shared model. Minimized and governed jointly. |
| **Anti-Corruption Layer** | Translating between external models (payment gateways, carriers, e-mail providers) and the internal domain. Mandatory for every third-party integration. |
| **Conformist** | Downstream context adopts the upstream model as-is. Only acceptable for generic/commodity subdomains. |
| **Open Host Service** | Upstream context exposes a well-defined protocol for multiple consumers. |
| **Published Language** | A shared schema (integration event, API contract) used across contexts. Must be versioned. |
| **Customer/Supplier** | Upstream and downstream negotiate the contract through an explicit change process. |
| **Separate Ways** | Contexts are fully independent. No integration needed. |

### 2.2 Tactical Design

| Pattern | Mandatory Behavior |
| --- | --- |
| **Aggregate** | Consistency boundary for invariants. All mutations go through the aggregate root. Keep aggregates small. |
| **Entity** | Domain object with unique identity and lifecycle. Business behavior belongs on the entity, not in external services. |
| **Value Object** | Immutable, identity-less object compared by value (`Money`, `Address`, `Sku`, `Email`, `DateRange`). |
| **Domain Event** | Immutable record of something that happened in the domain, named in past tense using ubiquitous language. |
| **Domain Service** | Stateless operation that does not naturally belong to an entity or value object. |
| **Repository** | Abstraction for aggregate persistence. The Domain layer defines the interface; Infrastructure provides the EF Core implementation. |
| **Factory** | Encapsulates complex creation logic for aggregates when constructors are insufficient. |
| **Specification** | Encapsulates a business rule as a reusable, composable predicate object. |

### 2.3 Folder and File Organization by Domain

The physical structure must reflect Bounded Contexts and domain concepts — not technical layers.

#### 2.3.1 Top-Level Structure

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
├── SharedKernel/
└── Program.cs            # Composition root: wires every context
```

The repository is a single deployable (`Portfolio.csproj`, root namespace `Portfolio`). Bounded-context isolation is enforced by folder structure, namespaces, and architecture tests ([Section 10.2](#102-architecture-fitness-functions)). If a context is later extracted into its own `.csproj`, record it in an ADR.

#### 2.3.2 Internal Bounded Context Structure

Namespaces follow folders: `Portfolio.<BoundedContext>.<Layer>[.<Aggregate>]` (e.g., `Portfolio.Ordering.Domain.Order`). Interfaces use the `I` prefix.

```
src/Ordering/
├── Domain/
│   ├── Order/
│   │   ├── Order.cs                  # Aggregate root
│   │   ├── OrderItem.cs              # Entity within the aggregate
│   │   ├── OrderStatus.cs            # Value object or typed enum
│   │   ├── OrderPlaced.cs            # Domain event
│   │   └── IOrderRepository.cs       # Repository interface
│   └── Services/
│       └── PricingService.cs         # Domain service
├── Application/
│   ├── Commands/
│   │   ├── PlaceOrderCommand.cs
│   │   └── PlaceOrderHandler.cs
│   ├── Queries/
│   │   ├── GetOrderByIdQuery.cs
│   │   └── GetOrderByIdHandler.cs
│   └── DTOs/
│       └── OrderSummaryDto.cs
├── Infrastructure/
│   ├── Persistence/
│   │   ├── OrderRepository.cs        # IOrderRepository implementation (EF Core)
│   │   ├── OrderingDbContext.cs      # One DbContext per bounded context
│   │   └── Configurations/           # IEntityTypeConfiguration<T> mappings
│   ├── Messaging/
│   │   └── OrderEventPublisher.cs    # RabbitMQ publisher / consumers
│   └── ExternalServices/
│       └── PaymentGatewayAdapter.cs  # Anti-Corruption Layer
└── API/
    ├── Controllers/
    │   └── OrdersController.cs
    └── Contracts/
        ├── PlaceOrderRequest.cs
        └── OrderResponse.cs
```

Each context exposes one `Add<Context>Module(this IServiceCollection, IConfiguration)` extension (and, if needed, a `Use<Context>Module` counterpart) that registers its own services; `Program.cs` only calls these extensions.

#### 2.3.3 Mandatory Folder Rules

| Rule | Mandatory Behavior |
| --- | --- |
| **Domain at the center** | `Domain/` must never reference `Infrastructure/`, `API/`, EF Core, ASP.NET Core, RabbitMQ, Valkey, or OpenTelemetry types. |
| **One aggregate per folder** | Each aggregate root and its related entities, value objects, and events live in a dedicated folder. |
| **Repository interfaces in Domain** | Repository interfaces belong in `Domain/`. Implementations belong in `Infrastructure/Persistence/`. |
| **No cross-context direct references** | Bounded Contexts must not reference each other's `Domain`, `Application` (except published contracts), or `Infrastructure` types. Use integration events, a context's published public interface, or the Shared Kernel. |
| **Shared Kernel is minimal and governed** | `SharedKernel/` contains only truly shared primitives (base `Entity`/`AggregateRoot`, `Money`, `Result`, domain-event and integration-event abstractions, clock abstraction). It must not contain business logic of any single context. |
| **Infrastructure is replaceable** | The Infrastructure layer must be swappable without changing Domain or Application code. |
| **API contracts are separate from DTOs** | External API contracts (`API/Contracts`) and internal application DTOs (`Application/DTOs`) are distinct and must not be mixed. |
| **Test structure mirrors source structure** | Test folders mirror source organization by Bounded Context and aggregate. |

#### 2.3.4 Anti-Patterns in Folder Organization

The following structures are **forbidden**:

```
# FORBIDDEN: Organization by technical layer at the top level
src/
├── Controllers/       # Groups by technical role, not domain
├── Services/          # Ambiguous — which domain?
├── Models/            # Mixes domain models with DTOs
├── Repositories/      # No domain context
└── Helpers/           # Catch-all without cohesion

# FORBIDDEN: Flat structure without domain boundaries
src/
├── OrderController.cs
├── OrderService.cs
├── OrderModel.cs
├── ProductController.cs
├── ProductService.cs
└── ProductModel.cs
```

</DomainDrivenDesign>

---

<SOLIDAsArchitecturalDiscipline>
## 3. SOLID as Architectural Discipline

SOLID principles, as defined in [CODE.md Section 1.1](./CODE.md), are mandatory at every level. This section extends them from class-level design to architectural boundaries.

### 3.1 SOLID at the Architectural Level

| Principle | Class-Level (CODE.md) | Architectural-Level (This Document) |
| --- | --- | --- |
| **S — Single Responsibility** | One class, one reason to change. | One Bounded Context, one business capability. One layer, one architectural concern. |
| **O — Open/Closed** | Extend by composition, not modification. | Contexts accept new features via extension points (event handlers, strategy injection) without core changes. |
| **L — Liskov Substitution** | Subtypes honor parent contracts. | Infrastructure implementations are fully substitutable for their domain-defined abstractions without behavior surprises. |
| **I — Interface Segregation** | Focused interfaces, not broad contracts. | A context's public contracts are focused per consumer. Avoid monolithic service facades. |
| **D — Dependency Inversion** | Depend on abstractions, not concretions. | Domain and Application define interfaces. Infrastructure provides implementations. Never invert this flow. |

### 3.2 Mandatory SOLID Enforcement

- **SRP at module level:** Every Bounded Context owns a single cohesive business capability. If a context starts serving unrelated capabilities, split it.
- **OCP at integration level:** Adding an integration, event handler, or rule variant must not require modifying existing context internals.
- **LSP at infrastructure level:** Swapping an infrastructure implementation (e.g., RabbitMQ for another broker, Valkey for an in-memory cache in tests) must not require changes in Domain or Application.
- **ISP at API level:** Consumers must not be forced to depend on API operations they do not use. Split large API surfaces into focused contracts.
- **DIP at every boundary:** Domain never imports from Infrastructure or API. Application never imports from Infrastructure except through interfaces defined in Domain or Application.

### 3.3 Architectural Violations to Block in Review

| Violation | Description |
| --- | --- |
| **Domain importing Infrastructure** | Domain classes reference EF Core attributes, `HttpClient`, RabbitMQ types, or ASP.NET Core types. |
| **God Module** | A single context handles multiple unrelated business domains. |
| **Leaky Abstraction** | Infrastructure concerns (connection strings, queue names, SQL) exposed through domain interfaces. |
| **Monolithic Service Interface** | A single interface exposes 20+ methods serving unrelated consumers. |
| **Concrete Dependency in Application Layer** | Handlers instantiate infrastructure classes (`new DbContext()`, `new ConnectionFactory()`) instead of using injected abstractions. |

</SOLIDAsArchitecturalDiscipline>

---

<RichDomainModeling>
## 4. Rich Domain Modeling

The domain model is the heart of the system. It must be rich, expressive, and self-enforcing — never a passive collection of data containers. These standards extend [BUSINESS.md](./BUSINESS.md).

### 4.1 Aggregate Design Rules

| Rule | Mandatory Behavior |
| --- | --- |
| **Small Aggregates** | Include only the entities and value objects needed to enforce invariants. Prefer smaller over larger. |
| **Consistency Within, Eventual Across** | Strong consistency within an aggregate. Cross-aggregate consistency uses domain/integration events and eventual consistency. |
| **Identity References Only** | Cross-aggregate (and cross-context) relationships use identifiers (strongly typed IDs), never direct object references or EF navigation properties. |
| **Root as Gatekeeper** | All external interaction goes through the aggregate root. Internal entities are not directly accessible. |
| **Invariant Enforcement** | Aggregates validate and enforce all invariants on every state change. Invalid state must be impossible to persist. |
| **Concurrency Control** | Aggregates implement optimistic concurrency (a `Version` field). See [DATABASE.md](./DATABASE.md). |
| **One Aggregate per Transaction** | A single transaction creates or modifies exactly one aggregate instance. Changes spanning aggregates are coordinated through events and eventual consistency, never a shared database transaction. |

### 4.2 Entity and Value Object Rules

| Concept | Mandatory Behavior |
| --- | --- |
| **Entity** | Encapsulates behavior, not just data. Public setters or getters without behavior indicate an anemic model. Business methods belong on the entity. |
| **Value Object** | Immutable (`record` / `readonly record struct`), equality by value, validates its own invariants at construction. Invalid values are rejected. |
| **Enums** | Use typed enumerations or value objects for domain concepts with a fixed set of values. Avoid primitive obsession. |

#### Entity Anti-Patterns (Forbidden)

```csharp
// ANTI-PATTERN: Anemic model — entity is a passive data bag
public class Order
{
    public Guid Id { get; set; }                 // mutable from anywhere
    public decimal Total { get; set; }           // primitive, unprotected
    public string Status { get; set; }           // any value accepted
    public List<OrderItem> Items { get; set; }   // exposed collection bypasses invariants
}
// All business logic lives in an external service.
```

```csharp
// PREFERRED: Rich model — entity enforces its own invariants
public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderItem> _items = [];

    public Money Total { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.Draft;
    public IReadOnlyList<OrderItem> Items => _items;

    public Result AddItem(ProductId productId, Quantity quantity, Money unitPrice)
    {
        if (Status != OrderStatus.Draft)
            return Result.Failure(OrderErrors.NotEditable);

        _items.Add(new OrderItem(productId, quantity, unitPrice));
        Total = Money.Sum(_items.Select(i => i.Subtotal));
        return Result.Success();
    }

    public Result Place()
    {
        if (_items.Count == 0) return Result.Failure(OrderErrors.Empty);
        if (Total.Amount <= 0) return Result.Failure(OrderErrors.ZeroTotal);

        Status = OrderStatus.Placed;
        Raise(new OrderPlaced(Id, Total));
        return Result.Success();
    }
}
```

### 4.3 Domain Services

Domain services follow [BUSINESS.md Section 4.3](./BUSINESS.md): used only for logic that does not belong to a single entity or value object, stateless, dependent only on domain abstractions, and never a catch-all "manager" or "helper".

### 4.4 Domain Events

Domain events follow [BUSINESS.md Section 4.2](./BUSINESS.md):

- Named in past tense using ubiquitous language (`OrderPlaced`, `PaymentReceived`).
- Immutable once created (`sealed record`).
- Carry sufficient data for consumers to act without querying back to the source.
- Versioned when published externally.
- Traceable to the business rule or state transition that triggered them.
- Carry a minimal metadata envelope: unique event ID, originating aggregate ID and version, and an occurred-on UTC timestamp. Integration events additionally carry correlation and causation IDs so a business flow can be traced across contexts.
- Collected inside the aggregate (`Raise(...)`) and dispatched by the Application/Infrastructure layer after the aggregate is saved — never dispatched from inside the domain.

### 4.5 Anti-Patterns to Avoid

| Anti-Pattern | Description | Correction |
| --- | --- | --- |
| **Anemic Domain Model** | Entities are pure data bags; all logic lives in external services. | Move business behavior into entities and aggregates. |
| **Primitive Obsession** | Raw `string`/`int`/`decimal` for Money, Email, Sku, Status. | Create value objects that encapsulate validation and semantics. |
| **God Aggregate** | One aggregate owns too many entities and invariants. | Decompose into focused aggregates with eventual consistency. |
| **Logic in Application Layer** | Business rules in command handlers instead of the domain. | Push rules down into entities, value objects, and domain services. |
| **Domain depending on Infrastructure** | Domain classes use EF Core attributes, HTTP types, or messaging classes. | Keep the domain pure; map with `IEntityTypeConfiguration<T>` in Infrastructure. |
| **Exposed Collections** | Aggregates expose mutable collections. | Return `IReadOnlyList<T>`; mutate only through aggregate methods. |

</RichDomainModeling>

---

<Modularity>
## 5. Modularity as a Base Principle

### 5.1 Module Boundaries

| Rule | Mandatory Behavior |
| --- | --- |
| **Business-Aligned Boundaries** | Module boundaries align with Bounded Contexts, not technical concerns. |
| **Explicit Public API** | Every context defines an explicit public surface (integration events, and where needed a small set of public interfaces/contracts). Everything else is `internal`. Default visibility for types is `internal`; `public` requires a reason. |
| **No Circular Dependencies** | Circular dependencies between contexts are forbidden. Use dependency inversion or events to break cycles. |
| **Independent Deployability (goal)** | Contexts are structured so they can be extracted into independent deployables if ever needed, even while they ship as one. |
| **Own Data Ownership** | Each context owns its data. Direct cross-context table access is forbidden. |

### 5.2 Module Communication

| Communication Type | When to Use | Rules |
| --- | --- | --- |
| **Synchronous (in-process call)** | The caller needs an immediate response (query, validation). | Call a public interface owned by the target context. Never access its internal types or tables. |
| **Asynchronous (integration events over RabbitMQ)** | The caller does not need an immediate response (notifications, projections, cross-context reactions). | Events are immutable and versioned, published via the transactional outbox ([DATABASE.md Section 3.1](./DATABASE.md)), and consumed idempotently. |
| **Shared Kernel** | Contexts genuinely share a small set of types. | Minimal, stable, governed jointly. |

### 5.3 Module Independence

| Aspect | Mandatory Behavior |
| --- | --- |
| **Build/Test Independence** | A context's unit tests run without any other context's infrastructure being present. |
| **Schema Independence** | Each context owns its own PostgreSQL schema and its own `DbContext`. Cross-context table sharing is forbidden. |
| **Configuration Isolation** | Context-specific configuration lives in its own configuration section (`Catalog:*`, `Ordering:*`, ...). |
| **Failure Isolation** | A failure in one context (or in Valkey/RabbitMQ) must not cascade. Apply timeouts, retries, circuit breakers, and bulkheads in Infrastructure (Polly via `Microsoft.Extensions.Http.Resilience` / `Microsoft.Extensions.Resilience`). |

### 5.4 Modular Monolith as Default

- The architecture is a **modular monolith**: one deployable unit with strictly enforced module boundaries.
- Microservice extraction must be driven by proven scaling, deployment, or team-ownership needs and recorded in an ADR — not assumed.
- A context that is a candidate for extraction must already comply with every rule in this section.

</Modularity>

---

<LayeredArchitecture>
## 6. Layered Architecture

Within each Bounded Context, layers enforce separation of concerns and dependency direction.

### 6.1 Mandatory Layers

| Layer | Responsibility |
| --- | --- |
| **Domain** | Business logic: entities, value objects, aggregates, domain events, repository interfaces, domain services. |
| **Application** | Use cases, command/query handlers, orchestration, DTOs, application-level validation, transaction scope. |
| **Infrastructure** | EF Core persistence (Npgsql), RabbitMQ publishers/consumers, Valkey caching, external service adapters. |
| **API** | ASP.NET Core controllers, request/response contracts, input deserialization, authentication/authorization attributes. |

### 6.2 Dependency Direction

Dependencies flow **inward**:

```
API  →  Application  →  Domain  ←  Infrastructure
```

- **Domain** depends on nothing external (only `SharedKernel`). It defines interfaces that Infrastructure implements.
- **Application** depends on Domain (and `SharedKernel`). It defines use cases.
- **Infrastructure** depends on Domain and Application to implement interfaces. It is never depended upon by Domain or Application.
- **API** depends on Application. It translates HTTP requests into commands/queries. Composition (DI wiring) happens in each context's module registration and in `Program.cs`.

### 6.3 Layer Responsibility Matrix

| Concern | Domain | Application | Infrastructure | API |
| --- | :---: | :---: | :---: | :---: |
| Business invariants | Yes | | | |
| Aggregate behavior | Yes | | | |
| Repository interfaces | Yes | | | |
| Domain events | Yes | | | |
| Use case orchestration | | Yes | | |
| Command/Query dispatch | | Yes | | |
| Application DTOs | | Yes | | |
| Transaction management | | Yes | Yes | |
| Database access (EF Core) | | | Yes | |
| Caching (Valkey) | | | Yes | |
| External API calls | | | Yes | |
| Messaging / event publishing (RabbitMQ) | | | Yes | |
| EF Core mapping configuration | | | Yes | |
| HTTP routing | | | | Yes |
| Request/Response contracts | | | | Yes |
| Input deserialization | | | | Yes |
| Authentication/authorization middleware | | | | Yes |

</LayeredArchitecture>

---

<APIAndContractDesign>
## 7. API and Contract Design

### 7.1 General API Rules

| Rule | Mandatory Behavior |
| --- | --- |
| **Contract-First** | Design the contract (routes, schemas, errors) before implementation. The generated OpenAPI document (`/api/v1/openapi/v1.json`) is the contract of record and is served through Scalar (`/api/v1/docs`) in development only. |
| **Versioning** | All public APIs are versioned in the URL path (`/api/v1/...`). Breaking changes require a new version, not in-place modification. |
| **Backward Compatibility** | Existing API versions remain backward-compatible during their supported lifecycle. |
| **Input Validation at Boundary** | All external input is validated at the API layer before reaching Application or Domain. |
| **No Domain Leakage** | API contracts never expose domain types, aggregate structures, or infrastructure details. |
| **Idempotent Writes** | State-changing endpoints that clients may retry (checkout, payment, order placement) support an `Idempotency-Key` header so retries do not duplicate side effects. |
| **Error Contracts** | Errors use RFC 9457 Problem Details. See [BUSINESS.md Section 5.3](./BUSINESS.md). |
| **Documented in OpenAPI** | Every endpoint declares its success and error responses (`ProducesResponseType`/`TypedResults`), auth requirements, and examples. |

### 7.2 CQRS Alignment

- **Commands** (writes) go through the Application layer to Domain aggregates.
- **Queries** (reads) may bypass the Domain layer and read optimized projections/views (EF Core `AsNoTracking` projections or SQL views) when performance justifies it.
- Separate command and query models when read and write patterns diverge significantly. CQRS is recommended, not mandatory, for every context.

### 7.3 Security Alignment

- All API design complies with [SECURITY.md](./SECURITY.md), including OWASP API Security Top 10 mitigations.
- Authentication, authorization, rate limiting, and input validation are enforced at the API boundary.

</APIAndContractDesign>

---

<CrossCuttingConcerns>
## 8. Cross-Cutting Concerns

Cross-cutting concerns are implemented consistently across contexts without leaking into domain logic.

| Concern | Mandatory Approach |
| --- | --- |
| **Logging** | Structured logging via `ILogger<T>` with correlation/trace IDs propagated across HTTP calls and RabbitMQ messages. Never log sensitive data. See [SECURITY.md](./SECURITY.md) Section 11 and [OBSERVABILITY.md](./OBSERVABILITY.md). |
| **Authentication** | Handled at the API/Infrastructure boundary. Never inside domain logic. |
| **Authorization** | Business-level rules in the domain ([BUSINESS.md](./BUSINESS.md) Section 8). Technical enforcement at the API boundary. |
| **Caching** | Infrastructure layer (Valkey), with explicit TTL and invalidation. Never in the Domain layer. |
| **Transaction Management** | Scoped at the Application layer. One transaction per use case. The Domain is transaction-unaware. |
| **Resilience** | Timeouts, retries, and circuit breakers in Infrastructure. Domain logic contains no retry logic. |
| **Idempotency** | Command handlers and RabbitMQ consumers are idempotent, using an idempotency key or event-ID deduplication to tolerate retries and at-least-once delivery. |
| **Telemetry** | Metrics and traces are collected at Infrastructure and API layers without polluting domain code. |
| **Validation** | Input validation at the API boundary. Domain invariants in the domain. Database constraints as a safety net. |
| **Time** | All time access goes through `TimeProvider`; never call `DateTime.UtcNow` directly outside the composition root. |

</CrossCuttingConcerns>

---

<InfrastructureAndExternalDependencies>
## 9. Infrastructure and External Dependencies

### 9.1 Infrastructure Isolation

| Rule | Mandatory Behavior |
| --- | --- |
| **Anti-Corruption Layer** | Every external system integration (payment gateway, carrier, e-mail/SMS provider) uses an ACL translating between external and internal models. |
| **Adapter Pattern** | Infrastructure implementations are adapters for domain-defined interfaces. |
| **No Vendor Lock-in in Domain** | Domain and Application do not reference vendor SDKs or framework types. |
| **Configuration Externalization** | Connection strings, endpoints, credentials, and feature flags are externalized (environment variables, user secrets locally, a secrets manager in production). Never hardcoded. Use the options pattern with validation on start (`ValidateOnStart`). |
| **Secrets Management** | All secrets follow [SECURITY.md](./SECURITY.md) Section 5. |

### 9.2 Database Architecture Alignment

- Database design follows [DATABASE.md](./DATABASE.md).
- A single PostgreSQL database is used, with **one schema per Bounded Context** and **one EF Core `DbContext` per context**.
- Cross-context data access goes through context APIs or integration events — never cross-schema queries or foreign keys.

### 9.3 Messaging and Event Infrastructure

- Event publishing and consumption are implemented in Infrastructure (`Infrastructure/Messaging`) using RabbitMQ.
- Domain events are defined in Domain; their serialization and transport are Infrastructure concerns.
- Message contracts are versioned and backward-compatible.
- Dead-letter exchanges/queues and poison-message handling are defined for every queue.
- Consumers are idempotent: delivery is assumed at-least-once, so reprocessing a message (identified by its event ID) must not duplicate side effects.
- Events are published through the transactional outbox ([DATABASE.md Section 3.1](./DATABASE.md)); integration-event schemas are defined as JSON Schema (or AsyncAPI) and stored in the repository.
- RabbitMQ topology (exchanges, queues, bindings, DLQs) is declared in code or definitions files that are version-controlled — not created by hand.

### 9.4 Messaging, Outbox, and Workflow Libraries

| Need | Recommended |
| --- | --- |
| **RabbitMQ client** | `RabbitMQ.Client` (raw) |
| **Messaging abstraction with EF Core outbox** | Wolverine, DotNetCore.CAP, or Rebus. MassTransit 9+ is commercial — do not adopt without approval. |
| **In-process command/query dispatch** | A small custom dispatcher or a source-generated mediator (e.g., `martinothamar/Mediator`, MIT). MediatR 13+ is commercial — do not adopt without approval. |
| **Resilience** | `Microsoft.Extensions.Resilience` / `Microsoft.Extensions.Http.Resilience` (Polly v8) |
| **Durable workflows / sagas** | Only if required: Wolverine sagas or Temporal .NET SDK; record the choice in an ADR. |

The messaging library is selected once, project-wide, through an ADR.

</InfrastructureAndExternalDependencies>

---

<Evolvability>
## 10. Evolvability and Migration Strategy

### 10.1 Evolutionary Architecture Rules

| Rule | Mandatory Behavior |
| --- | --- |
| **Fitness Functions** | Automated architecture tests validate structural rules on every build. |
| **Incremental Change** | Architectural changes are applied incrementally. No big-bang rewrites without formal approval and a risk plan. |
| **Feature Toggles for Migration** | Use feature flags for gradual rollout of architectural changes. See [BUSINESS.md](./BUSINESS.md) Section 11. |
| **Backward-Compatible Contracts** | API and event contract changes are backward-compatible during transition windows. |

### 10.2 Architecture Fitness Functions

Automated checks that run in CI:

- **Dependency direction tests:** Domain does not reference Infrastructure, API, EF Core, ASP.NET Core, RabbitMQ, or Valkey client namespaces.
- **Module boundary tests:** A context does not reference another context's internals (only `SharedKernel` and published contracts).
- **Cyclic dependency detection:** The build fails if cycles appear between contexts.
- **Naming and visibility tests:** Domain events are named in past tense; handlers end in `Handler`; types are `internal` unless intentionally public.
- **Layer violation tests:** No layer bypasses its allowed dependencies.

### 10.3 Fitness Function Tooling

| Tool | Use |
| --- | --- |
| **NetArchTest.Rules** or **ArchUnitNET** | Dependency, naming, and visibility rules written as xUnit tests in `tests/Architecture`. Rules are namespace-based because all contexts share one project. |
| **Roslyn analyzers / `Directory.Build.props`** | `TreatWarningsAsErrors`, analyzer rules per [CODE.md](./CODE.md). |

</Evolvability>

---

<ArchitectureDecisionRecords>
## 11. Architecture Decision Records

### 11.1 ADR Requirements

Every significant architectural decision is recorded in an ADR:

| Attribute | Description |
| --- | --- |
| **ID** | Sequential identifier (`ADR-001`). |
| **Title** | Concise description of the decision. |
| **Status** | Proposed, Accepted, Deprecated, or Superseded. |
| **Context** | The problem that motivated the decision. |
| **Decision** | The choice that was made. |
| **Consequences** | Known trade-offs, risks, and impacts. |
| **Reversibility** | Two-way door (easily reversible) or one-way door (costly to reverse). Guides how much analysis the decision warrants. |
| **Alternatives** | Other options considered and why they were rejected. |
| **Date** | When the decision was made. |
| **Decision Makers** | Who participated. |

Decisions that require an ADR include: messaging library, mediator/dispatcher choice, subdomain classification, extracting a context into its own project/service, any exception to these standards with lasting effect.

### 11.2 ADR Process

- ADRs live in `docs/adr/`.
- ADRs are immutable once accepted; changes require a new ADR that supersedes the original.
- Anyone can propose an ADR. Acceptance requires maintainer review.
- Reference ADRs in code comments or documentation when the related decision is not obvious.

</ArchitectureDecisionRecords>

---

<DefinitionOfDone>
## 12. Architecture Definition of Done

A delivery that impacts architecture is complete only when all items below are true:

1. The solution is organized by Bounded Contexts, with folder structure reflecting business capabilities — not technical layers.
2. SOLID principles are respected at class and module level, with no known violations in the changed code.
3. The domain model is rich: entities encapsulate behavior, value objects enforce invariants, aggregates protect consistency boundaries.
4. Module boundaries are explicit, with no circular dependencies and no cross-context internal access.
5. Dependency direction is enforced: Domain depends on nothing; Infrastructure implements domain-defined interfaces.
6. All external integrations use Anti-Corruption Layers or well-defined adapters.
7. API contracts are versioned, backward-compatible, documented in OpenAPI, and do not leak domain internals.
8. Cross-cutting concerns are implemented without polluting domain code.
9. Architecture fitness functions validate structural rules in CI.
10. Relevant ADRs are created or updated.
11. Security requirements from [SECURITY.md](./SECURITY.md) are addressed.
12. Schema ownership and separation follow [DATABASE.md](./DATABASE.md).
13. Business rules are implemented in the Domain layer following [BUSINESS.md](./BUSINESS.md).
14. Test structure mirrors the domain-aligned source structure.

Any exception to these standards must be documented with owner, scope, risk, rationale, and expiration date.

</DefinitionOfDone>

---

_Last updated: September 30, 2026_
_These standards must be reviewed and updated at minimum once per year or after any significant architecture incident._

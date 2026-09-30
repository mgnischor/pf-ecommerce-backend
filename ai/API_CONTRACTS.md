# API Contract and Consumer Experience Standards

> **Scope:** This repository contains **no user interface**. It is the HTTP API (ASP.NET Core, .NET 10) consumed by storefront, back-office, and mobile clients that are built elsewhere. Those clients own their own visual design, accessibility (WCAG), and front-end testing. This document therefore governs the **API as the product's user experience boundary**: how the API behaves, reports errors, paginates, formats data, localizes content, protects users, and documents itself — so that any client can deliver a clear, accessible, consistent, and safe experience on top of it. If a front end is ever added to this repository, a UI addendum (accessibility, design system, component and E2E testing) must be introduced through an ADR. Every item marked as mandatory is a hard requirement unless a documented exception is approved by the maintainers.

---

## Table of Contents

1. [General Principles](#1-general-principles)
2. [Resource Design and Consistency](#2-resource-design-and-consistency)
3. [Request and Response Conventions](#3-request-and-response-conventions)
4. [Validation and Error Contract](#4-validation-and-error-contract)
5. [States, Feedback, and Asynchronous Operations](#5-states-feedback-and-asynchronous-operations)
6. [Pagination, Filtering, and Sorting](#6-pagination-filtering-and-sorting)
7. [Localization and Formatting](#7-localization-and-formatting)
8. [Performance from the Consumer's Perspective](#8-performance-from-the-consumers-perspective)
9. [Security and Privacy in the Contract](#9-security-and-privacy-in-the-contract)
10. [Content and Writing Standards](#10-content-and-writing-standards)
11. [Destructive and High-Impact Actions](#11-destructive-and-high-impact-actions)
12. [API Documentation, Evolution, and Contract Testing](#12-api-documentation-evolution-and-contract-testing)
13. [Recommended Libraries and Tooling](#13-recommended-libraries-and-tooling)
14. [API Contract Definition of Done](#14-api-contract-definition-of-done)

---

<GeneralPrinciples>
## 1. General Principles

| Principle | Mandatory Behavior |
| --- | --- |
| **Consumer Clarity First** | Every endpoint communicates clearly what it does, what state the resource is in, and what the consumer can do next (links, allowed actions, error codes). Ambiguity is a defect. |
| **Consistency** | Naming, status codes, error shapes, pagination, and formats are identical across all bounded contexts. A consumer who learned one endpoint can predict the others. |
| **Feedback for Every Request** | Every request produces an unambiguous outcome: a success with a resource or an explicit empty result, or a typed error. No silent failures and no `200` carrying an error. |
| **Fail Gracefully** | Errors, unavailable dependencies, and edge cases return RFC 9457 Problem Details with guidance — never stack traces, raw exceptions, or `500` for client mistakes. |
| **Domain Language** | Resource names, fields, enum values, and messages use the ubiquitous language of [BUSINESS.md](./BUSINESS.md) and [ARCHITECTURE.md](./ARCHITECTURE.md). |
| **Contract Over Implementation** | The API contract never exposes entities, aggregates, database identifiers, or infrastructure details ([ARCHITECTURE.md Section 7.1](./ARCHITECTURE.md)). |
| **Security in Plain Sight** | Security behavior (masking, step-up authentication, rate limits, session expiry) is communicated through explicit, documented responses. See [SECURITY.md](./SECURITY.md). |
| **No Friction Without Purpose** | Every required field, extra round trip, or constraint has a justification. |
| **Progressive Disclosure** | Endpoints return what the current task needs; detail and expansions are opt-in. |
| **Testability** | Every status code, error, and state is verifiable by automated tests ([TESTS.md](./TESTS.md)). |

</GeneralPrinciples>

---

<ResourceDesign>
## 2. Resource Design and Consistency

| Rule | Mandatory Behavior |
| --- | --- |
| **Versioned base path** | All routes start with `/api/v{major}/` (currently `/api/v1/`). Breaking changes require a new major version ([Section 12](#12-api-documentation-evolution-and-contract-testing)). |
| **Resource-oriented URLs** | Plural, lowercase, kebab-case nouns in domain language: `/api/v1/products`, `/api/v1/carts/{cartId}/items`, `/api/v1/orders/{orderId}`. No verbs in paths except for explicit domain commands (below). |
| **Domain commands** | Operations that are not CRUD are modeled as sub-resources or command endpoints named for the domain action: `POST /orders/{orderId}/cancellation`, `POST /carts/{cartId}/checkout`, `POST /payments/{paymentId}/refunds`. They return the created/updated resource or a `202` operation resource. |
| **Bounded-context alignment** | A route belongs to exactly one bounded context (`Catalog`, `Cart`, `Ordering`, …); cross-context composition is done by the consumer or by an explicit composition endpoint owned by one context. |
| **HTTP methods** | `GET` (safe, cacheable, no side effects), `POST` (create/command), `PUT` (full replace, idempotent), `PATCH` (partial update, JSON Merge Patch or explicit patch contract), `DELETE` (idempotent). |
| **Status codes** | `200` read/update with body; `201` create (+ `Location`); `202` accepted for asynchronous work; `204` success without body; `400` malformed request; `401` unauthenticated; `403` authenticated but forbidden; `404` not found (also used to hide resources the caller may not know exist); `405`; `409` conflict (state transition or concurrency); `412` precondition failed; `415`; `422` business-rule/semantic validation failure; `429` rate limited; `503` dependency unavailable (+ `Retry-After`). `5xx` is never returned for caller mistakes. |
| **Identifiers** | Public IDs are UUIDs (strings). Never expose sequential database keys. Human-readable references (order number) are separate fields. |
| **Self-describing responses** | Resources include `id`, current `status` (when stateful), and `version`/ETag; stateful resources expose the actions currently allowed (e.g., `allowedActions: ["cancel", "pay"]`) so clients do not duplicate state-machine rules ([BUSINESS.md Section 6](./BUSINESS.md)). |
| **Idempotency** | Safe and idempotent method semantics are respected; unsafe retried `POST`s accept `Idempotency-Key` ([Section 11](#11-destructive-and-high-impact-actions)). |
| **Separate contracts from DTOs** | Request/response types live in each context's `API/Contracts`; they are not application DTOs or domain types. |

</ResourceDesign>

---

<RequestResponseConventions>
## 3. Request and Response Conventions

| Topic | Mandatory Convention |
| --- | --- |
| **Media type** | `application/json; charset=utf-8` (`application/problem+json` for errors). Other media types are rejected with `415`. |
| **Property naming** | `camelCase` JSON properties (`System.Text.Json` web defaults, source-generated contexts). One naming policy for the whole API. |
| **Enums** | Serialized as `camelCase` strings (`"awaitingPayment"`), never numeric values. New values are additive; clients are told to tolerate unknown values. |
| **Dates and times** | Instants as ISO 8601 / RFC 3339 UTC with `Z` (`2026-11-30T23:59:59Z`). Calendar dates as `YYYY-MM-DD`. Durations as ISO 8601 (`PT15M`). Time zones, when needed, as IANA IDs. |
| **Money** | `{ "amount": "25.90", "currency": "BRL" }` — `amount` is a **decimal string** (never a JSON number/float) with the currency's minor-unit scale; `currency` is ISO 4217. Totals are computed by the server; clients never send authoritative totals ([SECURITY.md Section 2.3](./SECURITY.md)). |
| **Quantities and units** | Integers for discrete quantities; units explicit in the field name or an accompanying field (`weightGrams`). |
| **Nulls and absence** | Absent and `null` have documented meanings; optional fields are omitted rather than returned as empty strings. Empty collections are `[]`, never `null`. |
| **Unknown input** | Unknown properties on write requests are rejected (`400`) on security- or money-sensitive endpoints and ignored elsewhere, consistently documented ([SECURITY.md Section 6.2](./SECURITY.md)). |
| **Partial updates** | `PATCH` semantics are documented per resource; a client can never change read-only or server-owned fields (`id`, `status`, `total`, `role`, `createdAt`). |
| **Concurrency** | Stateful resources return an `ETag` derived from the aggregate `version`; updates accept `If-Match`; a mismatch returns `412`, and a conflict detected at persistence returns `409` ([DATABASE.md Section 7.1](./DATABASE.md)). |
| **Headers** | Responses carry `X-Request-ID`/`traceparent` correlation; requests may supply `Idempotency-Key`, `If-Match`, `Accept-Language`. |
| **Field names ≠ internals** | No database column names, internal type names, or ORM artifacts in names or messages. |

</RequestResponseConventions>

---

<ValidationAndErrorContract>
## 4. Validation and Error Contract

Consumers render errors to people. The contract must give them everything needed to do that well without parsing prose. It is consistent with [BUSINESS.md Section 5.3](./BUSINESS.md).

### 4.1 Error Shape

All non-success responses use **RFC 9457 Problem Details** (`application/problem+json`), implemented with ASP.NET Core `IProblemDetailsService`/`TypedResults.Problem`, extended with:

| Member | Description |
| --- | --- |
| `type` | A stable URI identifying the problem class (documented, dereferenceable in the API docs). |
| `title` | A short, plain-language summary that is the same for every occurrence of the `type`. |
| `status` | The HTTP status code. |
| `detail` | An occurrence-specific, plain-language explanation safe to show to the end user. |
| `instance` | The request path. |
| `traceId` | The W3C trace ID, so support can find the request in telemetry (no other internal identifiers). |
| `errors[]` | For validation and business-rule failures: each item has `ruleId`, `field` (JSON Pointer such as `/items/0/quantity`, or `null` for object-level errors), `code` (machine-readable, stable, `UPPER_SNAKE_CASE`), `message` (plain language), and optional `params` (values for message templates, e.g., `{ "minimum": "10.00" }`). |

```json
{
    "type": "https://errors.example.com/business-rule-violation",
    "title": "The order violates a business rule.",
    "status": 422,
    "detail": "The order total must be at least 10.00 BRL.",
    "instance": "/api/v1/orders/7f3c9a2e-0b6d-4c1e-9a55-3d2f1b8c7e10",
    "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
    "errors": [
        {
            "ruleId": "BR-ORD-001",
            "field": "/total",
            "code": "ORDER_TOTAL_BELOW_MINIMUM",
            "message": "The order total must be at least 10.00 BRL.",
            "params": { "minimum": "10.00", "currency": "BRL" }
        }
    ]
}
```

### 4.2 Error Rules

| Rule | Mandatory Behavior |
| --- | --- |
| **Stable codes** | `code` values are part of the contract: documented, never reused for a different meaning, and never removed without a deprecation cycle. Clients branch on `code`, not on `message`. |
| **Actionable messages** | `detail`/`message` state what is wrong and, where possible, how to fix it ("Choose a quantity between 1 and 10."). Never display raw exception text, SQL, or internal identifiers. |
| **Field pointers** | Every field-level error carries a `field` pointer so clients can place the error next to the right input and move keyboard/screen-reader focus to it. |
| **All errors at once** | Validation returns every failing field in one response, not one per round trip. |
| **Status discrimination** | Distinguish `400` (malformed), `422` (well-formed but violates a rule), `401`/`403` (identity/permission), `404`, `409` (conflict), `412`, `429`, and `503`; consumers can tell transient from permanent failure (`Retry-After` on `429`/`503`). |
| **No information leakage** | Authentication and lookup errors are uniform (no account-enumeration differences); an unauthorized access to another customer's resource returns `404` ([SECURITY.md Section 2.3](./SECURITY.md)). |
| **Preserve the ability to retry** | Errors never leave partially applied state; consumers can resend the same request (with its `Idempotency-Key`) safely. |
| **Validation layering** | Format/range checks at the API boundary (FluentValidation); invariants in the domain; database constraints as a safety net ([BUSINESS.md Section 5.1](./BUSINESS.md)). Domain failures map to `422`/`409` through one central mapping, not per-controller `try/catch`. |
| **Sensitive inputs** | Validation errors never echo passwords, tokens, card data, or full tax IDs. |

</ValidationAndErrorContract>

---

<StatesAndAsyncOperations>
## 5. States, Feedback, and Asynchronous Operations

Consumers implement loading, empty, error, and success states. The API must make each one unambiguous.

| Situation | Required API behavior |
| --- | --- |
| **Empty result** | A collection query with no matches returns `200` with `"items": []` and pagination metadata — never `404` and never `null`. `404` means the *resource* does not exist. |
| **Long-running work** | Work that may exceed a few seconds (report export, bulk import, refund settlement) returns `202 Accepted` with a `Location` header pointing to an operation resource exposing `status` (`pending`, `running`, `succeeded`, `failed`), `progress` when real progress is known (no fake progress), `result`/`error`, and `retryAfter`. |
| **Eventual consistency** | When a write's effects propagate asynchronously (e.g., stock projection after an order), the response states the current state and the resource exposes a `status` the consumer can poll or subscribe to; docs describe the expected delay. |
| **Partial success** | Bulk endpoints return per-item outcomes (`207`-style body or a documented result list) — never an all-or-nothing `200` that hides failures. |
| **Dependency degraded** | When a non-critical dependency (cache, recommendations) is down the API responds normally (possibly with reduced data and a `Warning`-style field); when a critical dependency (PostgreSQL, payment provider) is down it returns `503` with `Retry-After`. |
| **Rate limiting** | `429` with `Retry-After` and `RateLimit-*` headers so clients can back off gracefully. |
| **Success confirmation** | Commands return the resulting resource state (or `204`) so the client can confirm the outcome without an extra read. |
| **Stale data** | Cacheable responses carry `ETag`/`Last-Modified` and `Cache-Control` so clients can show fresh-vs-stale states correctly. |
| **Client-visible state machines** | Order, payment, shipment, and cart statuses are documented with their allowed transitions in the OpenAPI description, in the same domain vocabulary as [BUSINESS.md Section 6](./BUSINESS.md). |

</StatesAndAsyncOperations>

---

<PaginationFiltering>
## 6. Pagination, Filtering, and Sorting

| Rule | Mandatory Behavior |
| --- | --- |
| **Every collection is paginated** | No unbounded list endpoints. Default `limit` 20, maximum 100 (documented); requests above the maximum are clamped or rejected consistently. |
| **Cursor pagination by default** | Responses carry `items`, `nextCursor` (opaque, signed or encrypted — never a raw key), and `hasMore`. Offset pagination (`page`, `pageSize`, `totalCount`) is allowed only for small, bounded, back-office collections. Backed by keyset queries ([DATABASE.md Section 3.1](./DATABASE.md)). |
| **Deterministic ordering** | Every list has a stable default order with a unique tie-breaker, so pages never duplicate or skip items. |
| **Sorting** | `sort=field` / `sort=-field` over a documented allowlist of fields; unknown fields return `400`. |
| **Filtering** | Named query parameters per field (`status=paid&createdFrom=2026-01-01`), validated against documented types and allowlists; no free-form query languages that could reach the database directly. |
| **Search** | Text search uses a documented `q` parameter with minimum length and result caps; rate-limited ([SECURITY.md Section 6.1](./SECURITY.md)). |
| **Expansion** | Related data is included only on request (`include=items,shipment`) from an allowlist, with bounded depth; default responses stay small. |
| **Counts** | Total counts are optional and expensive; return them only when requested (`includeTotal=true`) or for small collections. |

</PaginationFiltering>

---

<LocalizationAndFormatting>
## 7. Localization and Formatting

| Rule | Mandatory Behavior |
| --- | --- |
| **Codes first, prose second** | Clients localize from stable error `code`s and `params`. The API also returns a default-language `message`/`detail` for diagnostics and simple clients. |
| **Language negotiation** | The API honors `Accept-Language` for user-facing text it generates (error messages, notification previews, catalog content where translations exist), sets `Content-Language`, and falls back to the configured default culture (documented; `pt-BR` and `en` are the initial targets, to be confirmed by ADR). |
| **Externalized strings** | Server-generated user-facing strings live in resource files (`.resx` / `IStringLocalizer`), never hardcoded in controllers or domain code. Domain errors carry codes and parameters; localization happens at the API boundary. |
| **No pre-formatted values** | The API returns machine values (decimal-string money plus ISO currency, ISO dates, enum strings, E.164 phone numbers); **presentation formatting is the client's job** using locale-aware formatting. Where the API must produce text (e-mails, SMS, invoices), it uses culture-aware formatting with the recipient's stored culture and time zone. |
| **Text handling** | All text is UTF-8 and Unicode-normalized (NFC) on input; length limits are in characters, not bytes; names and addresses accommodate international formats (no assumptions about name structure or postal-code shape beyond per-country validation). |
| **Time zones** | Timestamps are UTC; any local-time business rule states its zone ([BUSINESS.md Section 9.1](./BUSINESS.md)). |
| **Catalog content** | Translatable catalog fields (name, description) are modeled as per-locale values with a defined fallback chain, not concatenated strings. |

</LocalizationAndFormatting>

---

<PerformanceUX>
## 8. Performance from the Consumer's Perspective

Perceived performance in every client is bounded by the API. These targets complement the technical baselines in [CODE.md Section 3](./CODE.md) and the SLOs in [OBSERVABILITY.md](./OBSERVABILITY.md) (initial targets; refine from measurements):

| Endpoint class | Target (server-side, p95) |
| --- | --- |
| **Catalog reads (list/detail/search)** | Under 200 ms |
| **Cart operations** | Under 250 ms |
| **Checkout and order placement** | Under 800 ms (excluding third-party payment latency, which is reported separately) |
| **Authentication** | Under 500 ms (Argon2id cost is bounded by design) |
| **Any synchronous endpoint** | Responses that would exceed 2 seconds become asynchronous (`202`, [Section 5](#5-states-feedback-and-asynchronous-operations)). |

Mandatory practices:

- Enable response compression (Brotli/gzip) and HTTP/2 at the edge; keep payloads minimal (no entity dumps, bounded `include`).
- Provide `ETag`/`If-None-Match` (`304`) and explicit `Cache-Control` for cacheable catalog resources; `no-store` for personal data ([SECURITY.md Section 6.1](./SECURITY.md)).
- Offer batch or expansion endpoints where a screen would otherwise need many sequential calls (e.g., cart with prices and promotions in one response).
- Avoid chatty contracts: no endpoint requires the client to make N+1 follow-up calls for a standard screen.
- Every endpoint has a documented timeout and respects cancellation (`HttpContext.RequestAborted`).
- Payload size limits and pagination caps are enforced ([Section 6](#6-pagination-filtering-and-sorting)).
- Performance regressions beyond 10% p95 on these classes block release ([TESTS.md Section 7](./TESTS.md)).

</PerformanceUX>

---

<SecurityAndPrivacyContract>
## 9. Security and Privacy in the Contract

UI-facing security requirements are defined in [SECURITY.md](./SECURITY.md). This section states their contract-level effect.

### 9.1 Data Exposure

- Responses contain only the data the caller is authorized to see ([BUSINESS.md Section 8](./BUSINESS.md)); response contracts are explicit allowlists, never serialized entities.
- Sensitive fields are masked by default: payment methods expose brand, last four digits, and expiry only; tax IDs and phone numbers are masked (`***.***.***-12`) unless the caller is entitled to the full value and the endpoint is explicitly documented as revealing it.
- Personal data never appears in URLs (path or query) or in `Location`/`Link` headers where avoidable; use opaque IDs.
- PII is excluded from analytics-friendly fields, logs, and event payloads beyond what the consumer needs ([SECURITY.md Section 11.2](./SECURITY.md)).

### 9.2 Authentication and Session Signals

- Token expiry is communicated (`expires_in` on token responses; `401` with `WWW-Authenticate: Bearer error="invalid_token"` when expired) so clients can refresh transparently or redirect to sign-in and return the user to their destination.
- Sensitive operations (password change, e-mail change, account deletion, adding a payment method, high-value refunds) require **step-up authentication**: the API answers `403` with `WWW-Authenticate: Bearer error="insufficient_user_authentication"` (RFC 9470) and a Problem Details `type` the client maps to a re-authentication prompt.
- Concurrent-session information is available where supported (session list endpoint) so clients can warn users.
- Account-recovery and verification flows use single-use, short-lived tokens and uniform responses ([SECURITY.md Section 2.3](./SECURITY.md)).

### 9.3 Input and Output Safety

- User-generated text (reviews, names, addresses, notes) is stored as plain text and returned with its content type; consumers must output-encode it. HTML input is rejected or sanitized ([SECURITY.md Section 14.1](./SECURITY.md)); the API documents this so clients never render it as HTML.
- File upload endpoints validate type, size, and content server-side, authoritatively ([SECURITY.md Section 6.5](./SECURITY.md)); clients may pre-validate for convenience only.
- CORS is restricted to the configured allowlist ([SECURITY.md Section 6.4](./SECURITY.md)); cross-origin credentials use cookie authentication only with CSRF protection.
- Links returned to clients (redirect URLs, payment return URLs) are validated against allowlists on the server — no open redirects.

### 9.4 Privacy Controls

- Endpoints support data-subject rights: export of a customer's data, correction, consent management (marketing/cookies preferences), and deletion/anonymization ([DATABASE.md Section 9](./DATABASE.md)).
- Consent and preference state is stored and returned explicitly; nothing is opted in by default for non-essential processing.
- The API collects only the data declared in the privacy policy; new data fields require privacy review.

</SecurityAndPrivacyContract>

---

<ContentAndWriting>
## 10. Content and Writing Standards

Server-generated text is read by end users (through client UIs, e-mails, and SMS) and by developers (through the docs).

### 10.1 Voice and Tone

- Plain language at a general adult reading level; direct, respectful, consistent. Avoid jargon and acronyms unless they are defined in the ubiquitous language of [BUSINESS.md](./BUSINESS.md).
- Active voice. Never blame the user.

### 10.2 Messages and Microcopy

| Element | Rule |
| --- | --- |
| **Error `title`** | Constant per error type; short; names the problem ("The order violates a business rule."). |
| **Error `detail` / `message`** | Start with what went wrong, then how to fix it. Include the specific value or limit when useful (`params`). |
| **Enum/state labels** | Machine values are stable and in domain language (`awaitingPayment`); human labels are supplied by clients from localized resources keyed by the value. |
| **Notification content** | E-mails/SMS state exactly what happened, which order/object, what happens next, and how to get help. Transactional messages carry no marketing content without consent. |
| **Confirmation prompts (API-enforced)** | Name the specific object and consequence ([Section 11](#11-destructive-and-high-impact-actions)). |
| **API documentation text** | Describes purpose, preconditions, side effects, and failure modes per operation; uses the same terms as the domain. |

</ContentAndWriting>

---

<DestructiveActions>
## 11. Destructive and High-Impact Actions

Actions that delete data, trigger irreversible processes, or have financial or legal consequences need safeguards **enforced by the API**, not only by client UIs.

### 11.1 Identification

A destructive or high-impact action is any action that:

- Permanently deletes or anonymizes data (account deletion, address removal with open orders).
- Triggers a financial transaction (charge, refund, payout) or changes prices at scale.
- Sends communication to external parties (e-mails, SMS, carrier notifications).
- Transfers, revokes, or escalates permissions or access.
- Initiates a workflow that cannot be undone or has downstream side effects (order cancellation after fulfillment started, shipment creation).

### 11.2 Required Safeguards

| Safeguard | When Required |
| --- | --- |
| **Idempotency key** | Every unsafe request that may be retried (checkout, payment, refund, cancellation, account deletion) requires an `Idempotency-Key` header. The server stores the key with the request fingerprint and response; a replay returns the original result, and reuse with a different payload returns `422`. Keys expire after a documented window (e.g., 24 hours). Disabling a button in a client is a UX safeguard, not a substitute ([ARCHITECTURE.md Section 8](./ARCHITECTURE.md), [DATABASE.md Section 3.1](./DATABASE.md)). |
| **Precondition checks** | Commands that depend on the resource's current state require `If-Match` (or an explicit expected-status field) so a stale client cannot act on outdated information. |
| **Server-enforced confirmation** | Irreversible, significant actions (account deletion, bulk deletion, large refunds) require an explicit confirmation field in the body that names the target (e.g., `"confirmation": "DELETE ana@example.com"` or the order number) — "type-to-confirm" enforced at the API, so scripted clients cannot bypass it accidentally. The error for a missing or wrong confirmation states what must be supplied. |
| **Step-up authentication** | Required for the sensitive operations listed in [Section 9.2](#92-authentication-and-session-signals). |
| **Authorization and separation of duties** | High-value refunds and price changes require a second authorized actor or an approval state ([BUSINESS.md Section 8.1](./BUSINESS.md)). |
| **Undo or recovery path** | Where feasible, deletion is a soft delete with a documented recovery window and a restore endpoint; order cancellation and refund flows expose their compensation state. |
| **Dry-run / preview** | Bulk or pricing operations offer a `dryRun=true` (or preview resource) returning exactly what would change, before execution. |
| **Outcome description** | Responses to destructive actions describe precisely what was changed or deleted and what happens next (including asynchronous follow-up steps). |
| **Audit** | Every high-impact action is written to the audit log with actor, time, target, reason, and trace ID ([BUSINESS.md Section 13.3](./BUSINESS.md), [SECURITY.md Section 11](./SECURITY.md)). |

### 11.3 Alignment with Business Rules

- Error and confirmation semantics for financial or contractual actions reflect the business rule language in [BUSINESS.md](./BUSINESS.md) (`ruleId` in errors).
- State-transition endpoints enforce the aggregate's state machine and return `409` with the current status and `allowedActions` when the transition is not allowed.

</DestructiveActions>

---

<APIDocumentationAndEvolution>
## 12. API Documentation, Evolution, and Contract Testing

| Rule | Mandatory Behavior |
| --- | --- |
| **OpenAPI is the contract** | The generated OpenAPI document (`/api/v1/openapi/v1.json`, development only) is complete and accurate: every operation declares summary, description, parameters, request/response schemas, **all** documented error statuses with Problem Details schemas, security requirements, and realistic examples. |
| **Scalar as the living docs** | The Scalar UI (`/api/v1/docs`) is the consumer-facing reference in development. Published documentation for external/consumer teams is exported from the same OpenAPI file in CI ([SECURITY.md Section 6.6](./SECURITY.md)). |
| **Documented conventions** | The OpenAPI `info.description` (or a linked guide) documents authentication, pagination, error codes, idempotency, rate limits, localization, money/date formats, and state machines once, so operations do not repeat them. |
| **Error-code catalog** | All error `code`s and `type` URIs are listed in the docs with meaning and remediation. |
| **Backward compatibility** | Within a major version, only additive, non-breaking changes are allowed: new optional fields, new endpoints, new enum values (documented as open sets). Removing or renaming fields, changing types/semantics, tightening validation, or removing enum values is breaking and requires a new major version. |
| **Deprecation** | Deprecated operations are marked in OpenAPI (`deprecated: true`), emit `Deprecation` and `Sunset` headers (RFC 9745/RFC 8594) and `Link` to the migration guide, and remain available for the supported lifecycle (minimum 6 months) before removal. |
| **Contract tests** | The OpenAPI document is linted (Spectral), diffed for breaking changes (`oasdiff`), and exercised by property-based tests (Schemathesis) in CI ([TESTS.md Section 5](./TESTS.md)). |
| **Changelog** | Every API change appears in a consumer-facing changelog generated from pull requests. |
| **Sandbox and examples** | A seeded sandbox environment and runnable example requests (the repository's `.http` files or exported collections) exist for consumer teams; examples use realistic domain values and never real personal data. |
| **Client compatibility** | Breaking-change proposals identify affected consumers and a migration path in the pull request. |

</APIDocumentationAndEvolution>

---

<RecommendedLibraries>
## 13. Recommended Libraries and Tooling

| Requirement | Default |
| --- | --- |
| **OpenAPI generation** | `Microsoft.AspNetCore.OpenApi` (already referenced); document/operation/schema transformers for Problem Details, security schemes, and examples |
| **API reference UI** | `Scalar.AspNetCore` (already referenced), development only |
| **API versioning** | `Asp.Versioning.Http` / `Asp.Versioning.Mvc.ApiExplorer` (URL-path versioning) |
| **Problem Details** | ASP.NET Core `IProblemDetailsService` / `ProblemDetails` with a central exception and domain-`Result` to Problem Details mapper |
| **Request validation** | FluentValidation with a shared endpoint filter or MVC filter that maps failures to the `errors[]` contract |
| **Serialization** | `System.Text.Json` with source generation, camelCase policy, `JsonStringEnumConverter`, strict decimal handling |
| **Localization** | `Microsoft.Extensions.Localization` (`IStringLocalizer`, `.resx`), `RequestLocalizationMiddleware` for `Accept-Language`; ICU-based formatting through `CultureInfo` |
| **Caching / conditional requests** | `Microsoft.AspNetCore.OutputCaching` for safe public reads; ETag support via filters; `ResponseCompression` (Brotli) |
| **Rate limit headers** | `Microsoft.AspNetCore.RateLimiting` with a policy that emits `Retry-After` and `RateLimit-*` |
| **Idempotency** | A shared `Idempotency-Key` filter backed by the PostgreSQL idempotency table ([DATABASE.md Section 3.1](./DATABASE.md)) |
| **Linting / diffing / conformance** | Spectral, `oasdiff`, Schemathesis (see [TESTS.md Section 5](./TESTS.md)) |
| **Manual exploration** | The repository `.http` files (e.g., `Portfolio.http`), the VS Code REST Client/JetBrains HTTP Client, and Scalar's built-in client |
| **Client SDK generation (optional)** | Kiota or NSwag from the OpenAPI document, versioned and published from CI, with an ADR |

</RecommendedLibraries>

---

<DefinitionOfDone>
## 14. API Contract Definition of Done

A delivery that changes the API is complete only when all items below are true:

1. Routes, methods, and status codes follow [Section 2](#2-resource-design-and-consistency); the response never exposes domain, persistence, or infrastructure details.
2. Request and response conventions in [Section 3](#3-request-and-response-conventions) are followed (camelCase, UTC ISO dates, decimal-string money with ISO currency, string enums, UUIDs, ETags where stateful).
3. Validation and business-rule failures return the Problem Details contract with stable `code`s, `ruleId`, field pointers, and plain-language messages ([Section 4](#4-validation-and-error-contract)).
4. Empty, asynchronous, partial, degraded, and rate-limited situations are handled explicitly ([Section 5](#5-states-feedback-and-asynchronous-operations)).
5. Collections are paginated, deterministically ordered, and filter/sort through allowlists ([Section 6](#6-pagination-filtering-and-sorting)).
6. User-facing text is localizable from codes and resource files; no presentation formatting is hardcoded ([Section 7](#7-localization-and-formatting)).
7. The endpoint meets its latency class target or is asynchronous ([Section 8](#8-performance-from-the-consumers-perspective)).
8. Data exposure, masking, ownership checks, step-up authentication, and privacy controls follow [Section 9](#9-security-and-privacy-in-the-contract) and [SECURITY.md](./SECURITY.md).
9. Destructive and high-impact operations enforce idempotency keys, preconditions, server-side confirmation, step-up authentication, and audit logging as required by [Section 11](#11-destructive-and-high-impact-actions).
10. The OpenAPI document describes the change completely (all responses, examples, security), passes Spectral, and shows no unapproved breaking changes (`oasdiff`); deprecations carry `Deprecation`/`Sunset` metadata ([Section 12](#12-api-documentation-evolution-and-contract-testing)).
11. Contract, integration, and security tests cover every status code and error code introduced or changed ([TESTS.md](./TESTS.md)).
12. The consumer-facing changelog and error-code catalog are updated.

Any exception to these standards must be documented with owner, scope, risk, rationale, and expiration date.

</DefinitionOfDone>

---

_Last updated: September 30, 2026_
_These standards must be reviewed and updated at minimum once per year or after any significant API contract incident._

# Observability Standards

> **Scope:** These standards apply to the `pf-ecommerce-backend` application (C# / .NET 10 modular monolith), its background workers, and the platform components that collect and store its telemetry. The application emits **logs, metrics, and traces with OpenTelemetry** and exports them over **OTLP** (`OTEL_EXPORTER_OTLP_ENDPOINT`) to an OpenTelemetry Collector. Every item marked as mandatory is a hard requirement unless a documented exception is approved by the maintainers. Observability must make any production behavior explainable from telemetry alone — without attaching a debugger, adding log lines, or redeploying. Telemetry is production data: it complies with [SECURITY.md](./SECURITY.md), and the stack that collects it is infrastructure governed by [IAC.md](./IAC.md) and [CONTAINERS.md](./CONTAINERS.md).

---

## Table of Contents

1. [General Principles](#1-general-principles)
2. [Reference Stack and Architecture](#2-reference-stack-and-architecture)
    - 2.1 [Reference Stack](#21-reference-stack)
    - 2.2 [Telemetry Data Flow](#22-telemetry-data-flow)
    - 2.3 [Managed and Alternative Backends](#23-managed-and-alternative-backends)
3. [Instrumentation Standards](#3-instrumentation-standards)
    - 3.1 [OpenTelemetry as the Single Instrumentation API](#31-opentelemetry-as-the-single-instrumentation-api)
    - 3.2 [Mandatory Resource Attributes](#32-mandatory-resource-attributes)
    - 3.3 [Instrumentation and Architectural Layers](#33-instrumentation-and-architectural-layers)
    - 3.4 [Semantic Conventions and Custom Attributes](#34-semantic-conventions-and-custom-attributes)
    - 3.5 [SDK Configuration](#35-sdk-configuration)
4. [Logging Standards](#4-logging-standards)
    - 4.1 [Canonical Log Schema](#41-canonical-log-schema)
    - 4.2 [Severity Levels](#42-severity-levels)
    - 4.3 [Log Shipping Path](#43-log-shipping-path)
    - 4.4 [Log Volume Control](#44-log-volume-control)
5. [Metrics Standards](#5-metrics-standards)
    - 5.1 [Measurement Methods](#51-measurement-methods)
    - 5.2 [Naming and Units](#52-naming-and-units)
    - 5.3 [Instrument Types and Histograms](#53-instrument-types-and-histograms)
    - 5.4 [Cardinality Control](#54-cardinality-control)
    - 5.5 [Mandatory Metrics by Component](#55-mandatory-metrics-by-component)
    - 5.6 [Exposition](#56-exposition)
6. [Distributed Tracing Standards](#6-distributed-tracing-standards)
    - 6.1 [Context Propagation](#61-context-propagation)
    - 6.2 [Span Design](#62-span-design)
    - 6.3 [Sampling](#63-sampling)
    - 6.4 [Asynchronous Flows and the Outbox](#64-asynchronous-flows-and-the-outbox)
    - 6.5 [Correlating Signals](#65-correlating-signals)
7. [Continuous Profiling (Optional)](#7-continuous-profiling-optional)
8. [Client Correlation and Synthetic Monitoring](#8-client-correlation-and-synthetic-monitoring)
9. [SLIs, SLOs, and Error Budgets](#9-slis-slos-and-error-budgets)
    - 9.1 [Journey Tiers](#91-journey-tiers)
    - 9.2 [SLI Definitions](#92-sli-definitions)
    - 9.3 [SLOs as Code](#93-slos-as-code)
    - 9.4 [Error Budget Policy](#94-error-budget-policy)
10. [Alerting Standards](#10-alerting-standards)
    - 10.1 [Alert Design Principles](#101-alert-design-principles)
    - 10.2 [Burn-Rate Alerting](#102-burn-rate-alerting)
    - 10.3 [Mandatory Non-SLO Alerts](#103-mandatory-non-slo-alerts)
    - 10.4 [Severity and Routing](#104-severity-and-routing)
    - 10.5 [Alert Rule Requirements](#105-alert-rule-requirements)
11. [Dashboards](#11-dashboards)
12. [OpenTelemetry Collector Standards](#12-opentelemetry-collector-standards)
    - 12.1 [Deployment Topology](#121-deployment-topology)
    - 12.2 [Pipeline Rules](#122-pipeline-rules)
    - 12.3 [Collector Configuration](#123-collector-configuration)
13. [Deploying the Observability Stack](#13-deploying-the-observability-stack)
    - 13.1 [Infrastructure as Code and GitOps](#131-infrastructure-as-code-and-gitops)
    - 13.2 [Network Policies](#132-network-policies)
14. [Telemetry Security and Privacy](#14-telemetry-security-and-privacy)
    - 14.1 [Data Minimization and Redaction](#141-data-minimization-and-redaction)
    - 14.2 [Access Control and Tenancy](#142-access-control-and-tenancy)
    - 14.3 [Transport, Integrity, and Untrusted Input](#143-transport-integrity-and-untrusted-input)
15. [Retention and Cost Management](#15-retention-and-cost-management)
16. [.NET Instrumentation](#16-net-instrumentation)
17. [Testing Observability](#17-testing-observability)
18. [Local Development](#18-local-development)
19. [Operational Readiness and Incidents](#19-operational-readiness-and-incidents)
20. [Observability Definition of Done](#20-observability-definition-of-done)

---

<GeneralPrinciples>
## 1. General Principles

| Principle | Mandatory Behavior |
| --- | --- |
| **Observability by Design** | Telemetry is designed together with the feature, not added after an incident ([ARCHITECTURE.md §1](./ARCHITECTURE.md), [CODE.md §1](./CODE.md)). |
| **Open Standards** | Instrument with OpenTelemetry, transport with OTLP, propagate with W3C Trace Context. Vendor-specific agents and SDKs are forbidden in application code; changing a backend is a configuration change. |
| **Correlated Signals** | Logs, metrics, and traces share resource attributes and trace context so an engineer can pivot from an alert to the exact trace and log line. |
| **Symptom-Based Alerting** | Page on user-visible symptoms and SLO burn (a customer cannot check out), not on internal causes. Causes belong on dashboards and in runbooks. |
| **SLO-Driven Operations** | Every critical user journey has explicit SLOs; error budgets decide when reliability work outranks feature work. |
| **Telemetry Is Sensitive Data** | [SECURITY.md §11.2](./SECURITY.md#112-what-never-to-log) applies to every signal — log fields, span attributes, metric labels, baggage, and profile labels. |
| **Bounded Cost and Cardinality** | Every signal has a volume and cardinality budget. Unbounded label values, unsampled high-volume traces, and debug logging in production are defects. |
| **Everything as Code** | Collector configuration, dashboards, alert rules, SLOs, and retention policies are version-controlled, reviewed, and deployed through CI/CD. |
| **Domain Purity** | The Domain layer contains no telemetry code. Instrumentation lives in API and Infrastructure and in cross-cutting decorators ([ARCHITECTURE.md §8](./ARCHITECTURE.md#8-cross-cutting-concerns)). |
| **Telemetry Never Breaks the App** | Export is asynchronous, bounded, and failure-tolerant. An unavailable Collector or backend never fails, blocks, or slows a user request ([SECURITY.md §11.5.3](./SECURITY.md#1153-opentelemetry-integration-rules)). |

</GeneralPrinciples>

---

<ReferenceStackAndArchitecture>
## 2. Reference Stack and Architecture

### 2.1 Reference Stack

The application is **one deployable with two process roles** (API and worker — [CONTAINERS.md §6.1](./CONTAINERS.md#61-pod-design)). Telemetry is identified by service (`ecommerce-api`, `ecommerce-worker`) and by **bounded context** (`app.bounded_context`), not by one service per context.

| Layer | Default | Responsibility |
| --- | --- | --- |
| **Instrumentation** | OpenTelemetry .NET SDK and instrumentation libraries ([Section 16](#16-net-instrumentation)) | Produce traces, metrics, and logs from application code |
| **Collection and processing** | OpenTelemetry Collector ([Section 12](#12-opentelemetry-collector-standards)) | Receive OTLP, enrich, redact, sample, batch, route, and export |
| **Metrics storage** | Prometheus (OTLP receiver enabled); Grafana Mimir only when scale requires it | Metrics, recording rules, alert evaluation |
| **Log storage** | Grafana Loki (native OTLP ingest) | Operational logs; ruler for log-based alerts |
| **Trace storage** | Grafana Tempo | Traces |
| **Profile storage (optional)** | Grafana Pyroscope ([Section 7](#7-continuous-profiling-optional)) | Continuous profiles |
| **Security and audit logs** | A SIEM, or a separate restricted Loki tenant, plus the append-only audit table in PostgreSQL | Security and audit log categories ([SECURITY.md §11.3](./SECURITY.md#113-log-format-and-integrity)) |
| **Visualization** | Grafana | Dashboards, exploration, cross-signal navigation |
| **Alert routing** | Alertmanager | Grouping, inhibition, silencing, and routing to paging, chat, and ticketing |
| **SLO tooling (optional)** | Sloth (or Pyrra) | Generate SLI recording rules and multi-window burn-rate alerts ([Section 9.3](#93-slos-as-code)) |
| **Synthetic monitoring** | Prometheus Blackbox Exporter; Grafana k6 for scripted journeys | Probe critical journeys from outside |

| Requirement | Mandatory Behavior |
| --- | --- |
| **Pinned versions** | Component images are pinned by digest and Helm charts by exact version ([CONTAINERS.md §2.1](./CONTAINERS.md#21-base-image-selection)). The stack is upgraded through `development` → `staging` → `production`. |
| **License review** | Grafana, Loki, Tempo, Mimir, and Pyroscope are AGPL-3.0: approved as unmodified, separately deployed services. Modifying them requires review and an ADR ([SECURITY.md §9](./SECURITY.md#9-dependency-and-supply-chain-security)). Apache-2.0 alternatives: Thanos (metrics), Jaeger v2 (traces), OpenSearch (logs). |
| **End-of-life components** | Grafana Agent and Promtail are end-of-life and must not be introduced. The Collector's legacy `jaeger`, `logging`, and `loki` exporters are not used — export OTLP. |
| **One tool per purpose** | One metrics backend, one log backend, and one trace backend per environment. |
| **Right-sized for the deployment** | Single-host (Compose) and small-cluster deployments run the monolithic/single-binary modes of the stack; distributed modes (Mimir, distributed Loki/Tempo) require measured need. |

### 2.2 Telemetry Data Flow

```
 ecommerce-api / ecommerce-worker (OpenTelemetry .NET SDK)
   ├── traces ── OTLP/gRPC ──┐
   ├── metrics ─ OTLP/gRPC ──┼──►  OpenTelemetry Collector
   └── logs ──── OTLP/gRPC ──┘     memory_limiter → enrich → scrub/redact → filter → tail-sample → batch
        (stdout JSON kept for                │
         local diagnostics only)             ├──► Prometheus / Mimir   (metrics)
                                             ├──► Loki                 (operational logs)
                                             ├──► Tempo                (traces)
                                             └──► SIEM / restricted tenant (security & audit logs)
                                                          │
                                   Grafana ◄──────────────┘     Alertmanager ──► paging · chat · ticketing
```

### 2.3 Managed and Alternative Backends

A managed backend (Grafana Cloud, Azure Monitor, AWS CloudWatch/X-Ray, Google Cloud Observability, Datadog, Elastic, Honeycomb, or similar) may replace a self-hosted component only through an ADR ([ARCHITECTURE.md §11](./ARCHITECTURE.md#11-architecture-decision-records)) showing all of the following:

| Condition | Mandatory Behavior |
| --- | --- |
| **OTLP ingestion** | The backend accepts OTLP. Telemetry reaches it through the Collector, never directly from the application. |
| **No vendor code in the app** | Application code uses only the OpenTelemetry API. Vendor-supported OpenTelemetry distributions are allowed; proprietary tracers and agents are forbidden. |
| **Data residency and privacy** | The provider's data location and processing terms satisfy LGPD/GDPR ([DATABASE.md §9](./DATABASE.md#9-data-governance-and-compliance)). |
| **Parity with this document** | Retention, access control, alerting, and SLO requirements are met. |
| **Cost model and exit path** | The ADR includes projected monthly cost at current and 3× volume, and how telemetry would be re-routed by changing Collector exporters only. |

</ReferenceStackAndArchitecture>

---

<InstrumentationStandards>
## 3. Instrumentation Standards

### 3.1 OpenTelemetry as the Single Instrumentation API

| Rule | Mandatory Behavior |
| --- | --- |
| **OpenTelemetry API only** | Code creates spans and metric instruments through `System.Diagnostics.ActivitySource` and `System.Diagnostics.Metrics.Meter`, which the OpenTelemetry .NET SDK consumes natively. |
| **One mechanism per signal** | One instrumentation mechanism per signal; no second tracing or metrics library alongside OpenTelemetry. |
| **Library instrumentation first** | Use maintained instrumentation for ASP.NET Core, `HttpClient`, Npgsql, the Valkey client, and RabbitMQ. Write manual spans and metrics only for use cases, domain-significant operations, and business KPIs. |
| **Pinned and upgraded together** | The SDK, every instrumentation package, and the semantic-conventions version are pinned (Central Package Management, lock file) and upgraded together in one reviewed change. |
| **Initialize first, flush on exit** | The SDK is configured at host startup (`AddOpenTelemetry()`), and providers are disposed (flushing buffers) on shutdown within the grace period of [CONTAINERS.md §8.2](./CONTAINERS.md#82-graceful-shutdown). |
| **Stable signals by default** | Only stable OpenTelemetry signals and conventions in production; experimental components (e.g., profiling) require an ADR and a pinned version. |
| **Build-time instrumentation** | Instrumentation is part of the application image (scanned, signed, reproducible). Zero-code auto-instrumentation injected by an operator is not used. |

### 3.2 Mandatory Resource Attributes

Every signal carries these resource attributes; they are the join keys between signals and the labels used by every dashboard, alert, and SLO.

| Attribute | Required | Source | Rule |
| --- | --- | --- | --- |
| `service.name` | Yes | `OTEL_SERVICE_NAME` | `ecommerce-api` or `ecommerce-worker` (lowercase kebab-case, stable). |
| `service.namespace` | Yes | `OTEL_RESOURCE_ATTRIBUTES` | `ecommerce` (maps to the IaC `project` tag). |
| `service.version` | Yes | Build pipeline | Semantic version plus short Git SHA used in the image tag ([CONTAINERS.md §2.5](./CONTAINERS.md#25-image-tagging-and-versioning)): `1.8.2+a1b2c3d`. |
| `service.instance.id` | Yes | Pod UID (Downward API) or generated UUID | Unique per running process. |
| `deployment.environment.name` | Yes | `OTEL_RESOURCE_ATTRIBUTES` | Exactly `development`, `staging`, or `production` ([IAC.md §2.2](./IAC.md#22-naming-conventions)). |
| `app.owner` | Yes | `OTEL_RESOURCE_ATTRIBUTES` | Owning team (same as the IaC `owner` tag); drives alert routing. |
| `k8s.*` | Yes, on Kubernetes | Collector `k8sattributes` processor | Added by the Collector, not hand-written. |
| `cloud.*` | Yes, in cloud | Collector `resourcedetection` processor | Added by the Collector. |

**Bounded context attribution.** Because all contexts run in one process, spans, metrics, and log records that belong to a context carry `app.bounded_context` (`ordering`, `catalog`, …) as a **signal attribute** (not a resource attribute). The use-case decorator ([Section 3.3](#33-instrumentation-and-architectural-layers)) sets it on spans; context-owned metrics carry it as a bounded-value dimension; log records carry it through a logging scope set by the same decorator.

### 3.3 Instrumentation and Architectural Layers

Instrumentation follows [ARCHITECTURE.md §6](./ARCHITECTURE.md#6-layered-architecture). Telemetry is cross-cutting and must not leak into domain logic.

| Layer | Allowed | Forbidden |
| --- | --- | --- |
| **Domain** | Nothing. The domain raises domain events ([ARCHITECTURE.md §4.4](./ARCHITECTURE.md#44-domain-events)); telemetry is derived from them elsewhere. | Any dependency on OpenTelemetry, `ILogger`, `ActivitySource`, or `Meter`. |
| **Application** | Logging through `ILogger<T>` with `LoggerMessage` templates ([SECURITY.md §11.5.1](./SECURITY.md#1151-architecture-requirements)). Use-case spans and metrics are added by a decorator/pipeline behavior registered at the composition root. | Direct `ActivitySource`/`Meter` calls in handlers. |
| **API** | Library instrumentation of inbound requests; low-cardinality route template; attaching the authenticated actor ID as a span attribute. | Capturing request/response bodies or headers by default ([Section 6.2](#62-span-design)). |
| **Infrastructure** | Client instrumentation (Npgsql/EF Core, HTTP, RabbitMQ, Valkey), the outbox relay, domain-event subscribers that translate events into business metrics, implementations of the audit-log abstraction. | Business decisions based on telemetry state. |
| **Composition root** | SDK bootstrap: resource, sampler, propagators, exporters, views, and registration of every `ActivitySource`/`Meter` name. | — |

```csharp
// Good: a decorator registered at the composition root adds the use-case span.
// The command handler and the domain contain no telemetry code.
public sealed class TracingCommandHandlerDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner) : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private static readonly ActivitySource Source = new("Ecommerce.UseCases");

    public async Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        using var activity = Source.StartActivity($"{typeof(TCommand).Name}");
        activity?.SetTag("app.bounded_context", BoundedContextOf(typeof(TCommand)));
        try
        {
            return await inner.HandleAsync(command, cancellationToken);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.AddException(ex);
            throw;
        }
    }
}

// Good: a business KPI derived from a domain event in Infrastructure, in the Ubiquitous Language.
// Subscribers run after the transaction commits, so rolled-back work is never counted.
public sealed class OrderPlacedMetricsSubscriber(IMeterFactory meterFactory) : IDomainEventHandler<OrderPlaced>
{
    private readonly Counter<long> _ordersPlaced = meterFactory
        .Create("Ecommerce.Ordering")
        .CreateCounter<long>("ordering.orders.placed", unit: "{order}", description: "Orders accepted by the Ordering context.");

    public Task HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken)
    {
        _ordersPlaced.Add(1, new KeyValuePair<string, object?>("ordering.sales_channel", domainEvent.SalesChannel.ToString()));
        return Task.CompletedTask;
    }
}

// Forbidden: telemetry inside the aggregate couples the domain to infrastructure.
public sealed class Order
{
    public void Place()
    {
        OrderingMetrics.OrdersPlaced.Add(1);   // ✗
        _logger.LogInformation("Order placed"); // ✗
    }
}
```

Metrics are operational signals, not a system of record. Figures used for billing, reporting, or reconciliation come from the database ([BUSINESS.md §7](./BUSINESS.md#7-calculation-and-derivation-rules)).

### 3.4 Semantic Conventions and Custom Attributes

| Rule | Mandatory Behavior |
| --- | --- |
| **Semantic conventions first** | Anything covered by the OpenTelemetry semantic conventions (HTTP, database, messaging, exceptions, runtime, Kubernetes, cloud) uses their names and units. Inventing `status` or `db_query` is forbidden. |
| **Stable conventions** | Opt in to the stable HTTP/database conventions with `OTEL_SEMCONV_STABILITY_OPT_IN=http,database` where an instrumentation still emits older ones. |
| **Convention upgrades are changes** | Upgrading the conventions can rename attributes and metrics; the same pull request updates every affected dashboard, alert, recording rule, and SLO. |
| **Custom namespaces** | Organization-wide attributes use `app.` (`app.owner`, `app.correlation_id`, `app.bounded_context`); context attributes use the context name (`ordering.order.id`, `ordering.sales_channel`). Never use `otel.` or a convention-owned namespace. |
| **Naming syntax** | Lowercase, dot-separated namespaces, `snake_case` within a component. |
| **Ubiquitous Language** | Custom names use the domain's vocabulary ([ARCHITECTURE.md §2](./ARCHITECTURE.md#2-domain-driven-design-as-structural-foundation)). |

### 3.5 SDK Configuration

The SDK is configured through standard `OTEL_*` environment variables injected by the deployment, so the same image runs unchanged in every environment.

```yaml
# Deployment excerpt — application container (Kubernetes); Compose uses the same variable names
env:
    - name: POD_UID
      valueFrom:
          fieldRef:
              fieldPath: metadata.uid
    - name: OTEL_SERVICE_NAME
      value: ecommerce-api
    - name: OTEL_RESOURCE_ATTRIBUTES
      # a single line: whitespace between pairs is not portable across SDKs
      value: service.namespace=ecommerce,service.version=1.8.2+a1b2c3d,service.instance.id=$(POD_UID),deployment.environment.name=production,app.owner=team-platform
    - name: OTEL_EXPORTER_OTLP_ENDPOINT
      value: https://otel-collector.observability.svc.cluster.local:4317
    - name: OTEL_EXPORTER_OTLP_PROTOCOL
      value: grpc
    - name: OTEL_EXPORTER_OTLP_CERTIFICATE
      value: /var/run/otel/ca.crt
    - name: OTEL_PROPAGATORS
      value: tracecontext,baggage
    - name: OTEL_TRACES_SAMPLER
      value: parentbased_traceidratio
    - name: OTEL_TRACES_SAMPLER_ARG
      value: "1.0" # full head sampling; the Collector tail-samples (Section 6.3)
    - name: OTEL_METRICS_EXEMPLAR_FILTER
      value: trace_based
    - name: OTEL_METRIC_EXPORT_INTERVAL
      value: "30000"
    - name: OTEL_ATTRIBUTE_VALUE_LENGTH_LIMIT
      value: "2048"
    - name: OTEL_SEMCONV_STABILITY_OPT_IN
      value: http,database
```

| Setting | Mandatory Behavior |
| --- | --- |
| **Collector endpoint** | The application exports to the Collector's Service (or the node-local agent where an agent is deployed). Never directly to a backend. |
| **Transport security** | TLS (mTLS where the Collector requires client certificates) outside loopback; plaintext OTLP only on loopback and local development. |
| **Bounded buffers** | Keep the SDKs' bounded batch-processor queues; a full queue drops telemetry and increments a dropped-items metric instead of blocking callers. |
| **Attribute limits** | Attribute value length is limited (2048 characters) to protect the pipeline. |
| **No secrets in variables** | `OTEL_*` variables never contain credentials; backend credentials exist only in the Collector ([Section 12.2](#122-pipeline-rules)). If the Collector requires an API token from the application, it is mounted from the secrets manager via `OTEL_EXPORTER_OTLP_HEADERS_FILE`-style file injection, not plain environment values. |

</InstrumentationStandards>

---

<LoggingStandards>
## 4. Logging Standards

[SECURITY.md §11](./SECURITY.md#11-logging-and-monitoring) defines what must and must never be logged, the logging abstraction, and log integrity. This section defines the operational schema and plumbing.

### 4.1 Canonical Log Schema

Every log record carries the fields below. They are OpenTelemetry log attributes when exported over OTLP, and the same names in the JSON written to stdout.

| Field | OpenTelemetry log data model | Required | Rule |
| --- | --- | --- | --- |
| `timestamp` | `Timestamp` | Yes | ISO 8601 UTC, millisecond precision or better. |
| `severity` | `SeverityText` | Yes | `TRACE`, `DEBUG`, `INFO`, `WARN`, `ERROR`, `FATAL` ([Section 4.2](#42-severity-levels)). |
| `message` | `Body` | Yes | The rendered message; never secrets or personal data. |
| `message_template` | Attribute | Yes | The constant template before rendering (`Order {ordering.order.id} rejected: {ordering.rejection_reason}`) — the `{OriginalFormat}` of `LoggerMessage`. Enables grouping without regex. |
| `event.name` | `EventName` | Yes | Stable, low-cardinality event type in the Ubiquitous Language: `ordering.order.rejected` (set through `LoggerMessage` `EventId` name). |
| `app.log.category` | Attribute | Yes | `application`, `security`, or `audit` — drives routing to the security destination. |
| `logger` | `InstrumentationScope.Name` | Yes | Source category (`Ordering.Infrastructure.Outbox`), i.e., the `ILogger<T>` category. |
| `app.bounded_context` | Attribute | Yes, in context code | Set through a logging scope by the use-case decorator. |
| `trace_id`, `span_id` | `TraceId`, `SpanId` | Yes, when a span is active | Injected by the OpenTelemetry logging provider — never generated by hand. |
| `app.correlation_id` | Attribute | Yes | Business-flow correlation ID from the event envelope ([ARCHITECTURE.md §4.4](./ARCHITECTURE.md#44-domain-events)); see [Section 6.4](#64-asynchronous-flows-and-the-outbox). |
| `app.actor.id`, `app.actor.type` | Attributes | When an actor exists | Opaque user ID or service identity; `user` or `service`. Never a name, e-mail, or document number. |
| `exception.type`, `exception.message`, `exception.stacktrace` | Attributes | On errors | Stack traces are never returned to clients ([SECURITY.md §2.1](./SECURITY.md#21-owasp-top-102025-web-applications), A10). |
| Resource attributes ([Section 3.2](#32-mandatory-resource-attributes)) | `Resource` | Yes | Added by the SDK/Collector — not repeated by hand. |

```json
{
    "timestamp": "2026-09-30T14:03:12.481Z",
    "severity": "WARN",
    "message": "Order 5f1c9a2e rejected: out_of_stock",
    "message_template": "Order {ordering.order.id} rejected: {ordering.rejection_reason}",
    "event.name": "ordering.order.rejected",
    "app.log.category": "application",
    "app.bounded_context": "ordering",
    "logger": "Ordering.Application.Commands.PlaceOrderHandler",
    "trace_id": "4bf92f3577b34da6a3ce929d0e0e4736",
    "span_id": "00f067aa0ba902b7",
    "app.correlation_id": "0192f7a8-3c5e-7b41-9d0e-6f2a8c4b1e57",
    "app.actor.id": "usr_01J8Z3K4M5N6P7Q8R9S0T1V2W3",
    "app.actor.type": "user",
    "ordering.order.id": "5f1c9a2e",
    "ordering.rejection_reason": "out_of_stock"
}
```

### 4.2 Severity Levels

| Level | OTel `SeverityNumber` | Use for | Production default |
| --- | --- | --- | --- |
| `TRACE` | 1–4 | Step-by-step execution detail. | Disabled |
| `DEBUG` | 5–8 | Diagnostic detail for developers. | Disabled |
| `INFO` | 9–12 | Business-significant events and state transitions (`ordering.order.placed`), service lifecycle. | Enabled (minimum level) |
| `WARN` | 13–16 | Unexpected but handled situations: retries, fallbacks, degraded dependencies, expected business rejections. | Enabled |
| `ERROR` | 17–20 | A failed operation that needs attention: unhandled exception, exhausted retry, message sent to the dead-letter queue. | Enabled |
| `FATAL` | 21–24 | The process cannot continue and is about to exit. | Enabled |

- The production minimum level is `INFO`. `DEBUG` may be enabled for a single context/category through runtime configuration (`Logging:LogLevel:*` reload), time-boxed to at most 4 hours, never by deploying a debug build; the change itself is logged as an `audit` event.
- An exception is logged **once**, where it is handled. Log-and-rethrow at every layer is forbidden.
- Expected business failures (validation errors, rule rejections — [BUSINESS.md §5.3](./BUSINESS.md#53-validation-error-structure)) are `INFO` or `WARN`, never `ERROR`. `ERROR` means something is broken.

### 4.3 Log Shipping Path

| Channel | Behavior |
| --- | --- |
| **OTLP (canonical)** | The OpenTelemetry logging provider exports log records to the Collector over OTLP. This is the one shipping path to the operational and security backends, so each record is ingested exactly once and carries `trace_id`/`span_id` natively. |
| **Console (diagnostic)** | JSON to stdout for `kubectl logs`, `docker logs`, and local runs ([SECURITY.md §11.5.2](./SECURITY.md#1152-mandatory-output-channels)). **No log agent tails the application's stdout** — doing so would duplicate every record. Node/host log agents (if present for other workloads) exclude the application containers. |
| **File** | Disabled in containers ([CONTAINERS.md §3.5](./CONTAINERS.md#35-logging-and-observability)). |
| **Durability of critical events** | Security and audit events that must not be lost are written to the append-only PostgreSQL audit table in the same transaction as the action ([SECURITY.md §11.5.1](./SECURITY.md#1151-architecture-requirements)); the OTLP path is for search and alerting, not the system of record. |
| **Resilience** | OTLP export is bounded and non-blocking; when the Collector is unavailable, records are dropped from the export queue (counted in a dropped-items metric that alerts) while console output continues. |

- Multi-line plain-text output (for example, a framework writing a raw stack trace) is a defect: configure the framework's logging through the JSON formatter.

### 4.4 Log Volume Control

| Rule | Mandatory Behavior |
| --- | --- |
| **No logs in hot loops** | Never log per item inside loops, batch processing, or per-message fast paths. Count with a metric and log a summary. |
| **No probe noise** | `/health/*` requests are excluded from access logs and traces. |
| **Access logs at the edge** | Per-request access logs come from the ingress/reverse proxy. The application relies on traces and metrics for per-request data and logs only meaningful events. |
| **Line size limit** | A single record is at most 16 KiB; longer values are truncated with a marker. |
| **Repeated-error suppression** | Identical errors in a burst are rate-limited at the source, with a suppressed-count in the next emitted record. |
| **Budget** | The application has a daily log-ingest budget ([Section 15](#15-retention-and-cost-management)); exceeding 80% raises a `warning` ticket. |

</LoggingStandards>

---

<MetricsStandards>
## 5. Metrics Standards

### 5.1 Measurement Methods

| Method | Applies to | Signals |
| --- | --- | --- |
| **RED** | Every request- or message-driven component | Rate, Errors, Duration — derived from one duration histogram per operation. |
| **USE** | Every resource: CPU, memory, pools, queues | Utilization, Saturation, Errors ([CODE.md §3](./CODE.md#3-global-performance-baseline)). |
| **Four golden signals** | Overview dashboards and SLOs | Latency, traffic, errors, saturation. |
| **Business KPIs** | Every bounded context | At least one domain KPI per context, in the Ubiquitous Language ([Section 3.3](#33-instrumentation-and-architectural-layers)). |

### 5.2 Naming and Units

| Rule | Mandatory Behavior |
| --- | --- |
| **OpenTelemetry names** | Lowercase dot-separated names; the unit is in the instrument's `unit` field, not the name: `http.server.request.duration` with unit `s`. |
| **UCUM base units** | Durations in seconds (`s`), sizes in bytes (`By`), ratios as `1`, counts as annotations (`{request}`, `{order}`). Milliseconds and kilobytes are forbidden in new instruments. |
| **Prometheus translation** | The backend translates names deterministically: `http.server.request.duration` (`s`) → `http_server_request_duration_seconds`; counters gain `_total`. Dashboards and alerts query the translated name. |
| **Context prefix** | Custom metrics are prefixed with the bounded context or `app.`: `ordering.orders.placed`, `app.outbox.pending`. |
| **No values in names** | Dimension values never appear in names; use attributes (`ordering.sales_channel=web`). |

### 5.3 Instrument Types and Histograms

| Instrument | Use for | Rule |
| --- | --- | --- |
| **Counter** | Monotonic totals: requests, orders placed, messages processed | Never decremented. |
| **UpDownCounter** | Values that rise and fall with events: active requests, in-flight jobs | Incremented and decremented around the unit of work. |
| **Histogram** | Every duration and size distribution | Durations are always histograms — never gauges or averages. |
| **Gauge (observable)** | Sampled state: queue depth, pool size, oldest pending item age | Cheap, non-blocking callback. |

- Histogram bucket boundaries must include every latency threshold used by an SLO or alert (a 300 ms SLO needs a `0.3` boundary) — configure SDK views for `http.server.request.duration`.
- Prefer base-2 exponential histograms where the backend supports them; otherwise use the convention default boundaries adjusted by a view.

### 5.4 Cardinality Control

| Rule | Mandatory Behavior |
| --- | --- |
| **Forbidden dimensions** | User IDs, e-mails, session IDs, request/trace IDs, entity IDs (order, customer, SKU), raw URLs/paths, query strings, IP addresses, free-text error messages, and timestamps are never metric attributes. Use traces and logs. |
| **Bounded values only** | Attributes take values from small, known sets: `http.request.method`, `http.route` (the template), `http.response.status_code`, `error.type`, `outcome`, `app.bounded_context`. |
| **Series budget** | At most 1,000 series per metric per instance and 50,000 active series for the application; a breach of 80% raises a `warning` ticket. |
| **SDK cardinality limit** | Keep the SDK per-metric cardinality limit enabled; overflow (`otel.metric.overflow=true`) raises a `warning` ticket. |
| **Backend enforcement** | Ingestion and series limits configured in the metrics backend; `sample_limit`/`label_limit` on any Prometheus scrape config. |

### 5.5 Mandatory Metrics by Component

| Component | Mandatory metrics | Notes |
| --- | --- | --- |
| **HTTP server** | `http.server.request.duration` (method, route, status code), `http.server.active_requests` | Source of RED metrics and availability/latency SLIs. |
| **HTTP client** | `http.client.request.duration` per dependency (`server.address`) | Payment, carrier, e-mail/SMS providers. |
| **PostgreSQL (Npgsql/EF Core)** | `db.client.operation.duration`, connection-pool usage (`db.client.connection.count` by state, pending requests, wait time) | Server-side metrics in [DATABASE.md §10.2](./DATABASE.md#102-mandatory-metrics). |
| **Valkey cache** | Hit/miss ratio, latency, evictions, memory, and a cache-error counter (errors swallowed by the cache boundary) | [DATABASE.md §4.2](./DATABASE.md#42-general-caching-rules). |
| **RabbitMQ producer/consumer** | `messaging.client.sent.messages`, `messaging.client.consumed.messages`, `messaging.process.duration`, queue depth and consumer lag, dead-letter queue depth, `app.messaging.duplicates_discarded` | Duplicates discarded by idempotent consumers prove deduplication works ([DATABASE.md §3.1](./DATABASE.md#31-universal-sql-rules)). |
| **Outbox relay** | `app.outbox.pending` (gauge), `app.outbox.oldest_pending_age` (gauge, seconds), `app.outbox.publish.duration`, publish failures | The oldest pending age is the freshness SLI of integration events. |
| **Background jobs** | `app.job.runs` (job name, outcome), `app.job.duration`, `app.job.last_success_timestamp` (gauge) | Reservation expiry, cart cleanup, idempotency-key purge. |
| **Resilience policies** | Retry attempts, circuit-breaker state transitions, timeouts, rate-limiter rejections | From `Microsoft.Extensions.Resilience` and ASP.NET Core rate limiting. |
| **Runtime** | GC pauses and heap size, thread-pool queue length and saturation, exceptions per second | `OpenTelemetry.Instrumentation.Runtime`. |
| **Business KPIs** | At least one counter or histogram per context, e.g. `catalog.searches`, `cart.items.added`, `checkout.started`, `ordering.orders.placed`, `billing.payments.failed`, `inventory.reservations.expired`, `shipping.shipments.created`, `promotions.coupons.redeemed`, `notifications.sent` | Required by [CONTAINERS.md §11.1](./CONTAINERS.md#111-metrics). |

### 5.6 Exposition

| Rule | Mandatory Behavior |
| --- | --- |
| **OTLP push (default)** | All metrics are pushed over OTLP to the Collector — the .NET OpenTelemetry Prometheus exporter is not stable, and short-lived jobs cannot be scraped reliably. |
| **Scrape endpoint (optional)** | A Prometheus `/metrics` endpoint is enabled only when the platform requires scraping; it listens on a separate internal port, is not routed through the ingress, and is used **instead of** OTLP metrics, never in addition. |
| **Never both** | A metric is exported through exactly one path; pushing and scraping the same instruments double-counts. The Prometheus Pushgateway is forbidden. |

</MetricsStandards>

---

<DistributedTracingStandards>
## 6. Distributed Tracing Standards

### 6.1 Context Propagation

| Rule | Mandatory Behavior |
| --- | --- |
| **W3C standards** | Propagate `traceparent`/`tracestate` (Trace Context) and `baggage` on every hop. B3 or Jaeger propagators only at the boundary with a legacy system, as a composite propagator with a removal date. |
| **Every transport** | Context crosses HTTP and RabbitMQ. For messages, inject context into AMQP message headers; consumers extract it. |
| **Untrusted edge** | At the public edge, incoming `sampled` flags and `baggage` from clients are ignored and the sampling decision is made inside the trust boundary. A trace ID from a first-party client may be continued for correlation ([Section 8](#8-client-correlation-and-synthetic-monitoring)). |
| **Third-party egress** | Strip `baggage` from calls to third parties; send `traceparent` only when the partner contract requires it. |
| **Baggage content** | Only non-sensitive, low-cardinality routing hints. Never personal data, tokens, or authorization claims. |
| **No context loss** | Background work started within a request inherits its context; detached work without a parent is forbidden in request paths. |

### 6.2 Span Design

| Rule | Mandatory Behavior |
| --- | --- |
| **Low-cardinality names** | `{method} {route}` for HTTP (`GET /api/v1/orders/{orderId}`), `{operation} {collection}` for databases, `{operation} {destination}` for messaging (`process ordering.order-placed`), the command/query name for use cases (`PlaceOrderCommand`). IDs never appear in span names. |
| **Correct span kind** | `SERVER`/`CLIENT` for synchronous calls, `PRODUCER`/`CONSUMER` for messaging, `INTERNAL` for in-process steps. |
| **Status semantics** | `Error` status only for failures. For HTTP server spans `5xx` is an error and `4xx` is not. Expected business rejections are not errors: record `app.outcome=rejected`. |
| **Exceptions** | Record exceptions as span events (`exception.type`, `exception.message`, `exception.stacktrace`) and set `error.type`. |
| **Granularity** | One span per network call, use case, and significant internal step (work longer than about 10 ms). Never one span per loop iteration. |
| **Business identifiers** | Opaque entity IDs (`ordering.order.id`) are allowed on spans; personal data is not ([Section 14.1](#141-data-minimization-and-redaction)). |
| **Database statements** | `db.query.text` is recorded only in parameterized form, never with literal values. |
| **Headers and bodies** | Bodies are never captured. Headers only from an explicit allowlist; `Authorization`, `Cookie`, `Set-Cookie`, and API-key headers are always excluded. |
| **URLs** | Query strings are recorded only after redaction of sensitive parameters; credentials in URLs are forbidden ([SECURITY.md §5.2](./SECURITY.md#52-prohibited-practices)). |

### 6.3 Sampling

| Rule | Mandatory Behavior |
| --- | --- |
| **Head sampling** | SDKs use `parentbased_traceidratio` with ratio `1.0` so the Collector sees complete traces and can keep every error. A lower head ratio is allowed only above 1,000 spans per second per instance, by ADR. |
| **Tail sampling at the Collector** | Keep 100% of traces with an error, 100% of traces slower than the journey's SLO latency threshold, and a probabilistic baseline (default 10%) of the rest ([Section 12.3](#123-collector-configuration)). |
| **Trace-aware routing** | Tail sampling needs all spans of a trace in one Collector instance: run one tail-sampling Collector, or put a `loadbalancing` exporter tier (routing by trace ID) in front of several. |
| **Metrics are never sampled** | RED metrics and SLIs come from SDK metric instruments that see 100% of requests — never from sampled spans. |
| **Security and audit logs** | Never sampled or rate-limited ([SECURITY.md §11](./SECURITY.md#11-logging-and-monitoring)). |

### 6.4 Asynchronous Flows and the Outbox

| Rule | Mandatory Behavior |
| --- | --- |
| **Context stored with the event** | The outbox row stores the current `traceparent`/`tracestate` in the event metadata, written in the same transaction as the state change ([DATABASE.md §3.1](./DATABASE.md#31-universal-sql-rules)). |
| **Relay continues the trace** | The outbox relay creates the `PRODUCER` span from the stored context — not from its own polling loop — and injects it into the AMQP headers. |
| **Consumers** | A consumer's `process` span is a child of the producer context for single-message delivery and uses span links for batch delivery. |
| **Correlation ID vs. trace ID** | The trace ID identifies one technical execution; the correlation ID ([ARCHITECTURE.md §4.4](./ARCHITECTURE.md#44-domain-events)) identifies the business flow across retries, sagas, and days. Both are recorded on every log and span of the flow. |
| **Long-running flows** | Sagas (checkout → reserve stock → authorize payment → place order → ship) never keep one trace open for hours: each step is its own trace, joined by the correlation ID and span links. |
| **Idempotent retries** | A redelivered message produces a new `process` span with the same correlation ID; a discarded duplicate records `app.messaging.duplicate=true`. |

### 6.5 Correlating Signals

| Pivot | Mechanism |
| --- | --- |
| **Log → trace** | `trace_id` in every log record; Grafana derived fields link Loki to Tempo. |
| **Trace → logs** | Tempo's trace-to-logs configuration queries Loki by `trace_id` and service. |
| **Metric → trace** | Exemplars: `OTEL_METRICS_EXEMPLAR_FILTER=trace_based` and exemplar storage enabled in the metrics backend. |
| **Client/support → trace** | The Problem Details `traceId` and the `X-Request-ID` response header let support find a customer's failing request ([API_CONTRACTS.md §4](./API_CONTRACTS.md#4-validation-and-error-contract)). |
| **Alert → everything** | Every alert carries `service_name`, `deployment_environment_name`, and `bounded_context` labels and a `dashboard_url`. |

</DistributedTracingStandards>

---

<ContinuousProfiling>
## 7. Continuous Profiling (Optional)

Continuous profiling is recommended for the checkout/ordering/payment paths and optional elsewhere.

| Rule | Mandatory Behavior |
| --- | --- |
| **Evidence for optimization** | Any performance optimization cites profile or benchmark evidence ([CODE.md §1](./CODE.md#1-general-engineering-principles), Measure Before Optimizing). |
| **Mechanism** | Pyroscope .NET profiler in push mode, or on-demand `dotnet-trace`/`dotnet-counters`/`dotnet-gcdump` through an ephemeral debug container in staging ([CONTAINERS.md §11.3](./CONTAINERS.md#113-debugging-and-troubleshooting)). eBPF profilers require a documented exception. |
| **Overhead budget** | Below 2% CPU at p95 load, verified in load tests ([TESTS.md §7](./TESTS.md#7-performance-and-load-testing-standards)). |
| **Labels** | `service.name`, `service.version`, `deployment.environment.name`. |
| **Diagnostic endpoints** | Runtime diagnostic endpoints are never exposed on the service port or the ingress; the diagnostic IPC is disabled in production ([CONTAINERS.md §6.1](./CONTAINERS.md#61-pod-design)). |
| **Profiles are sensitive** | They follow the access control of [Section 14.2](#142-access-control-and-tenancy). |

</ContinuousProfiling>

---

<ClientCorrelationAndSyntheticMonitoring>
## 8. Client Correlation and Synthetic Monitoring

This repository has no front end and no real-user-monitoring SDK. The API supports client-side observability and watches journeys from outside:

| Rule | Mandatory Behavior |
| --- | --- |
| **Response correlation** | Every response carries `X-Request-ID` (and the W3C trace is reachable through it); Problem Details include `traceId`, so a customer-support ticket can be traced to the exact request. |
| **Continuing client traces** | The API continues a `traceparent` sent by first-party clients on an allowlisted set of origins ([SECURITY.md §6.4](./SECURITY.md#64-secure-and-efficient-cors-policy): `traceparent` is allowed in CORS only for those origins); from any other caller a new root trace is started and the incoming context is recorded as a link. |
| **Client performance data** | If first-party clients report Core Web Vitals or errors, they do so to their own telemetry pipeline; the API accepts no unauthenticated telemetry ingestion endpoint unless it is rate-limited, size-limited, and reviewed under [SECURITY.md §6](./SECURITY.md#6-http-and-edge-hardening). |
| **Synthetic journeys** | Critical journeys (browse catalog, sign in, add to cart, checkout in sandbox mode) are probed from outside the cluster in staging and production with Blackbox Exporter and scripted k6 checks, at least every minute for availability probes and every 5 minutes for scripted journeys. |
| **Synthetic data isolation** | Synthetic traffic uses dedicated accounts and test payment methods, is flagged (`app.synthetic=true`) so it can be excluded from business KPIs, and never triggers real fulfillment or charges. |
| **Privacy** | Correlation identifiers are opaque; no personal data is placed in headers or trace baggage ([Section 14.1](#141-data-minimization-and-redaction)). |

</ClientCorrelationAndSyntheticMonitoring>

---

<SLIsSLOsAndErrorBudgets>
## 9. SLIs, SLOs, and Error Budgets

### 9.1 Journey Tiers

The application is a single deployable, so SLOs are defined per **critical user journey** rather than per service. Each journey declares its tier in its SLO file and in the dashboard.

| Tier | Journeys (initial classification; confirm in an ADR) | Minimum availability SLO | Notification |
| --- | --- | --- | --- |
| **Tier 1 — Critical** | Checkout and order placement, payment authorization/capture, sign-in and token refresh | 99.9% | 24/7 paging (when staffed; see [Section 19](#19-operational-readiness-and-incidents)) |
| **Tier 2 — Standard** | Catalog browse/search, cart operations, order history and tracking, inventory reads, promotions evaluation | 99.5% | Ticket, paging only for `critical` burn during business hours |
| **Tier 3 — Internal** | Back-office administration, reviews, notifications delivery, reporting | 99% | Business hours |

### 9.2 SLI Definitions

SLIs are ratios of good events to valid events, measured as close to the caller as possible (ingress metrics preferred for availability).

| SLI | Good event | Valid event | Applies to |
| --- | --- | --- | --- |
| **Availability** | Response that is not `5xx`, not a timeout, and not a `429` caused by capacity | All requests except client errors (`4xx`) | Every synchronous journey |
| **Latency** | Request completed within the threshold (a histogram bucket boundary) | All successful requests | Every critical journey (thresholds from [API_CONTRACTS.md §8](./API_CONTRACTS.md#8-performance-from-the-consumers-perspective)) |
| **Freshness** | Outbox entry published / event processed within the threshold of being produced | All events | Outbox relay, consumers |
| **Correctness** | Reconciliation check that passed (e.g., payments vs. orders, stock vs. reservations) | All reconciliation checks | Payment and inventory flows |
| **Job success** | Scheduled run that completed within its deadline | All scheduled runs | Background jobs |

| Requirement | Mandatory Behavior |
| --- | --- |
| **Minimum coverage** | Every Tier 1 and Tier 2 journey has an availability SLO and a latency SLO; the outbox relay and consumers have a freshness SLO. |
| **Window** | Rolling 30 days. |
| **SLO stricter than SLA** | Any contractual SLA is looser than the internal SLO. |
| **Documented** | Each SLO records owner, SLI query, objective, window, rationale, and review date; reviewed quarterly and after any incident that consumed more than 20% of the budget. |
| **Performance tests** | Latency thresholds match those validated by load tests ([TESTS.md §7](./TESTS.md#7-performance-and-load-testing-standards)). |

### 9.3 SLOs as Code

SLOs live in the repository (`observability/slo/*.yaml`), are validated in CI, and are compiled into Prometheus recording and alerting rules deployed through GitOps.

```yaml
# observability/slo/checkout.yaml (Sloth spec)
version: "prometheus/v1"
service: "ecommerce-api"
labels:
    team: team-platform
    tier: "1"
    journey: checkout
slos:
    - name: "checkout-availability"
      objective: 99.9
      description: "Checkout requests are served without server errors."
      sli:
          events:
              error_query: >-
                  sum(rate(http_server_request_duration_seconds_count{service_name="ecommerce-api",
                  deployment_environment_name="production",
                  http_route=~"/api/v1/carts/{cartId}/checkout|/api/v1/orders", http_response_status_code=~"5.."}[{{.window}}]))
              total_query: >-
                  sum(rate(http_server_request_duration_seconds_count{service_name="ecommerce-api",
                  deployment_environment_name="production",
                  http_route=~"/api/v1/carts/{cartId}/checkout|/api/v1/orders"}[{{.window}}]))
      alerting:
          name: CheckoutAvailabilityBudgetBurn
          annotations:
              summary: "Checkout is burning its availability error budget"
              runbook_url: "https://runbooks.example.com/checkout/availability"
              dashboard_url: "https://grafana.example.com/d/checkout-journey"
          page_alert:
              labels:
                  severity: critical
          ticket_alert:
              labels:
                  severity: warning
```

- Resource attributes used in queries (`service.name`, `deployment.environment.name`) are promoted to labels at ingestion.
- The SLO specification format (Sloth, Pyrra, OpenSLO) is chosen once and recorded in an ADR. Plain Prometheus recording rules are acceptable until the tool is adopted.

### 9.4 Error Budget Policy

| Budget remaining (30-day window) | Required action |
| --- | --- |
| **More than 50%** | Normal delivery. |
| **25% to 50%** | Reliability items from post-incident reviews are prioritized in the next iteration. |
| **Less than 25%** | Releases touching the journey require a documented rollback plan and a second reviewer. |
| **Exhausted** | Feature releases affecting the journey are frozen until the budget recovers; only reliability fixes, security patches, and risk-reducing changes ship. |
| **Single incident consumed more than 20%** | A blameless post-incident review is mandatory, with corrective actions tracked to completion ([Section 19](#19-operational-readiness-and-incidents)). |

Waiving a freeze is an exception requiring owner, scope, risk, rationale, and expiration date.

</SLIsSLOsAndErrorBudgets>

---

<AlertingStandards>
## 10. Alerting Standards

### 10.1 Alert Design Principles

| Principle | Mandatory Behavior |
| --- | --- |
| **Symptom over cause** | Pages fire on user-visible symptoms (SLO burn, failing journeys). High CPU or a single pod restart is not a page unless it threatens an SLO. |
| **Actionable** | Every page requires human action now; otherwise it is a ticket or a dashboard panel. |
| **Owned** | Every alert has an owning team (`team` label) mapped to exactly one route. |
| **Documented** | Every alert links to a runbook ([CONTAINERS.md §11.3](./CONTAINERS.md#113-debugging-and-troubleshooting)) and a dashboard; an alert without a runbook fails CI. |
| **Quiet** | At most two pages per 12-hour on-call shift; alerts actionable less than half the time are tuned or removed in the monthly review. |
| **Grouped and inhibited** | Alertmanager groups by `alertname`, `service_name`, and `bounded_context`, and inhibits dependent alerts. |

### 10.2 Burn-Rate Alerting

SLO alerts use multi-window, multi-burn-rate conditions (both windows must exceed the burn rate); values apply to a 30-day window and are generated by the SLO tooling ([Section 9.3](#93-slos-as-code)).

| Severity | Long window | Short window | Burn rate | Budget consumed at trigger | Notification |
| --- | --- | --- | --- | --- | --- |
| `critical` | 1 hour | 5 minutes | 14.4× | 2% | Page |
| `critical` | 6 hours | 30 minutes | 6× | 5% | Page |
| `warning` | 1 day | 2 hours | 3× | 10% | Ticket |
| `warning` | 3 days | 6 hours | 1× | 10% | Ticket |

### 10.3 Mandatory Non-SLO Alerts

In addition to SLO alerts, the rules in [CONTAINERS.md §11.2](./CONTAINERS.md#112-alerting-rules) and the security alerts in [SECURITY.md §11.4](./SECURITY.md#114-alerting) are mandatory, together with:

| Condition | Severity |
| --- | --- |
| Outbox oldest pending event older than the freshness threshold (default 5 minutes) for 10 minutes | `critical` |
| RabbitMQ dead-letter queue not empty for 15 minutes | `warning` |
| Scheduled job without a successful run for twice its interval (`app.job.last_success_timestamp`) | `warning` |
| Consumer lag growing for 15 minutes on a Tier 1 flow (payment, order, stock reservation) | `critical` |
| PostgreSQL connection pool saturated (waiters > 0) for 5 minutes | `critical` |
| Stock reservation expiry job stalled, or negative-stock / oversell detection hit | `critical` |
| Payment webhook signature failures above baseline, or payment failure ratio anomaly | `critical` |
| Collector refusing or failing to export (`otelcol_receiver_refused_*`, `otelcol_exporter_send_failed_*`) for 10 minutes | `critical` |
| Collector exporter queue above 80% for 10 minutes; application OTLP dropped-items above zero for 10 minutes | `warning` |
| Expected metric absent (`absent()`) for a Tier 1 journey | `critical` |
| Cardinality budget or SDK overflow breached ([Section 5.4](#54-cardinality-control)) | `warning` |
| **Watchdog**: an always-firing alert routed to an external heartbeat monitor that pages when it stops arriving | `critical` |

The Watchdog alert is mandatory: a broken alerting pipeline must page through an independent path, not fail silently.

### 10.4 Severity and Routing

| Severity | Meaning | Notification | Response |
| --- | --- | --- | --- |
| `critical` | User-visible impact now or imminent | Page the on-call engineer | Acknowledge within 15 minutes |
| `warning` | Slow budget burn, degradation trend, capacity risk | Ticket and the team's alert channel | Per the rule; default next business day |
| `security` | Security detection from the SIEM | Security on-call | Per [SECURITY.md §12.1](./SECURITY.md#121-classification) |

- Informational conditions are dashboards, not alerts.
- Alertmanager configuration, receivers, and routes are code; receiver credentials come from the secrets manager.
- Silences carry an author, reason, and expiry of at most 24 hours.

### 10.5 Alert Rule Requirements

```yaml
apiVersion: monitoring.coreos.com/v1
kind: PrometheusRule
metadata:
    name: ordering-outbox
    namespace: ecommerce
spec:
    groups:
        - name: ordering-outbox
          rules:
              - alert: OrderingOutboxBacklogStale
                expr: >-
                    max by (service_name, deployment_environment_name)
                    (app_outbox_oldest_pending_age_seconds{service_namespace="ecommerce"}) > 300
                for: 10m
                labels:
                    severity: critical
                    team: team-platform
                    bounded_context: ordering
                annotations:
                    summary: "The outbox has events waiting for more than 5 minutes"
                    description: >-
                        The oldest pending outbox event in {{ $labels.service_name }} is
                        {{ $value | humanizeDuration }} old. Integration events are not reaching consumers,
                        so orders, payments, and stock may be out of sync.
                    runbook_url: "https://runbooks.example.com/platform/outbox-backlog"
                    dashboard_url: "https://grafana.example.com/d/outbox"
```

| Field | Mandatory Behavior |
| --- | --- |
| **Name** | PascalCase, prefixed with the bounded context or component, describing the symptom. |
| **`for` duration** | At least twice the evaluation interval. |
| **Labels** | `severity` and `team` are mandatory. |
| **Annotations** | `summary`, `description`, `runbook_url`, and `dashboard_url` are mandatory; the description states the user impact in the Ubiquitous Language. |
| **Tests** | Every rule has a `promtool test rules` unit test ([Section 17](#17-testing-observability)). |

</AlertingStandards>

---

<Dashboards>
## 11. Dashboards

| Rule | Mandatory Behavior |
| --- | --- |
| **Dashboards as code** | Provisioned from Git (Grafana Operator `GrafanaDashboard`, Grafana provisioning files, or the Terraform `grafana/grafana` provider — [IAC.md](./IAC.md)). |
| **No manual edits in production** | Edits are made in a non-production instance and promoted by pull request; a production UI edit is drift. |
| **Standard dashboards** | (1) Application overview: SLO status and remaining error budget, RED metrics, saturation, runtime metrics, top errors, deployment annotations. (2) One dashboard per critical journey (checkout, payment, sign-in, catalog). (3) Business KPIs per bounded context. (4) Dependencies: PostgreSQL, Valkey, RabbitMQ, outbox and consumers. (5) Telemetry pipeline health. |
| **Deployment annotations** | The deployment pipeline writes a Grafana annotation per production deployment (version, commit SHA). |
| **Ubiquitous Language** | Titles and descriptions use the domain vocabulary. |
| **Readable by everyone** | Every axis has a unit; default timezone UTC; thresholds shown as lines or text in addition to color; color-blind-safe palette. |
| **Focused** | About 20 panels at most, with variables for environment and bounded context. |
| **Hygiene** | Each dashboard has an owner folder and description; dashboards unused for 90 days are reviewed for deletion. |

</Dashboards>

---

<OpenTelemetryCollectorStandards>
## 12. OpenTelemetry Collector Standards

### 12.1 Deployment Topology

| Environment | Topology |
| --- | --- |
| **Local development** | The all-in-one image of [Section 18](#18-local-development) (Collector included). |
| **Docker Compose (single host)** | One `otel-collector` service on the internal network, receiving OTLP from `ecommerce-api`/`ecommerce-worker` and exporting to the backends. |
| **Kubernetes** | A Collector **Deployment** (at least 2 replicas in production, HPA, PDB) in the `observability` namespace acts as the gateway: enrich, scrub, filter, tail-sample, route, export. It is the only component holding backend credentials. A node **DaemonSet** agent is added only when node-level collection (host metrics, other workloads' logs) is needed; with more than one gateway replica, agents (or a dedicated front tier) use a `loadbalancing` exporter so all spans of a trace reach one tail-sampling instance. |
| **Sidecar** | Forbidden on Kubernetes (per-pod overhead). |

### 12.2 Pipeline Rules

| Rule | Mandatory Behavior |
| --- | --- |
| **Distribution** | A custom distribution built with `ocb` containing only the components in use, or the official `otelcol-k8s`/`otelcol-contrib` image. The image follows [CONTAINERS.md](./CONTAINERS.md): minimal base, pinned by digest, scanned, and signed. |
| **Processor order** | `memory_limiter` first; then enrichment (`k8sattributes`, `resourcedetection`, `resource`); then scrubbing and filtering (`transform`, `redaction`, `filter`); then `tail_sampling` (traces only); `batch` last. |
| **Memory limits** | `memory_limiter` as a percentage of the container memory limit; `GOMEMLIMIT` at about 80% of that limit. |
| **Bounded, persistent queues** | Every exporter uses `sending_queue` and `retry_on_failure`; queues are persisted with `file_storage` on a persistent volume in production. |
| **Receiver binding** | Receivers bind to the pod IP with TLS; the Collector requires client certificates (mTLS) on Kubernetes so only authorized senders can push. |
| **Credentials** | Backend credentials are read from files mounted from the secrets manager (`bearertokenauth` with `filename`), never from ConfigMaps or environment variables ([SECURITY.md §10.2](./SECURITY.md#102-kubernetes)). |
| **Debug extensions** | `pprof` and `zpages` disabled in production; `health_check` backs the probes. |
| **Self-monitoring** | The Collector's internal metrics are exported and alerted on ([Section 10.3](#103-mandatory-non-slo-alerts)). |
| **Validated in CI** | `otelcol validate --config=<file>` with the pinned distribution on every change. |
| **Least-privilege RBAC** | The ClusterRole grants only `get`, `list`, `watch` on the resources `k8sattributes` needs (pods, namespaces, replicasets); no wildcards. |

### 12.3 Collector Configuration

```yaml
extensions:
    health_check:
        endpoint: ${env:MY_POD_IP}:13133
    file_storage/queue:
        directory: /var/lib/otelcol/queue
    bearertokenauth/backends:
        filename: /var/run/secrets/observability/backend-token

receivers:
    otlp:
        protocols:
            grpc:
                endpoint: ${env:MY_POD_IP}:4317
                max_recv_msg_size_mib: 16
                tls:
                    cert_file: /etc/otel/tls/tls.crt
                    key_file: /etc/otel/tls/tls.key
                    client_ca_file: /etc/otel/tls/ca.crt # mTLS: only authorized senders may push

processors:
    memory_limiter:
        check_interval: 1s
        limit_percentage: 80
        spike_limit_percentage: 25
    k8sattributes:
        auth_type: serviceAccount
        extract:
            metadata: [k8s.namespace.name, k8s.pod.name, k8s.pod.uid, k8s.deployment.name, k8s.node.name]
        pod_association:
            - sources:
                  - from: resource_attribute
                    name: k8s.pod.uid
            - sources:
                  - from: connection
    resourcedetection:
        detectors: [env] # add the cloud detector chosen in the IaC ADR (eks, aks, gcp)
        override: false
    transform/scrub: # defense in depth; source-side redaction is the primary control (Section 14.1)
        error_mode: ignore
        trace_statements:
            - context: span
              statements:
                  - delete_matching_keys(attributes, "(?i).*(password|passwd|secret|token|authorization|cookie|api[_-]?key|card).*")
        log_statements:
            - context: log
              statements:
                  - delete_matching_keys(attributes, "(?i).*(password|passwd|secret|token|authorization|cookie|api[_-]?key|card).*")
    redaction:
        allow_all_keys: true
        blocked_values:
            - "[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}" # e-mail address
            - "\\b\\d{3}\\.?\\d{3}\\.?\\d{3}-?\\d{2}\\b" # CPF
            - "\\b\\d{2}\\.?\\d{3}\\.?\\d{3}/?\\d{4}-?\\d{2}\\b" # CNPJ
            - "\\b(?:\\d[ -]?){13,19}\\b" # payment card number
            - "(?i)bearer\\s+[a-z0-9._~+/-]+=*" # bearer token
        summary: info
    filter/operational-logs: # keep security and audit out of the operational store
        error_mode: ignore
        logs:
            log_record:
                - 'attributes["app.log.category"] == "security" or attributes["app.log.category"] == "audit"'
    filter/security-logs: # keep only security and audit for the security destination
        error_mode: ignore
        logs:
            log_record:
                - 'attributes["app.log.category"] != "security" and attributes["app.log.category"] != "audit"'
    tail_sampling:
        decision_wait: 10s
        num_traces: 100000
        expected_new_traces_per_sec: 500
        policies:
            - name: keep-errors
              type: status_code
              status_code:
                  status_codes: [ERROR]
            - name: keep-slow
              type: latency
              latency:
                  threshold_ms: 1000 # align with the strictest journey latency threshold
            - name: baseline
              type: probabilistic
              probabilistic:
                  sampling_percentage: 10
    batch:
        send_batch_size: 8192
        timeout: 5s

exporters:
    otlphttp/metrics: # Prometheus OTLP receiver or Mimir
        endpoint: https://metrics.observability.svc.cluster.local/otlp
        auth:
            authenticator: bearertokenauth/backends
        sending_queue:
            enabled: true
            storage: file_storage/queue
        retry_on_failure:
            enabled: true
    otlphttp/loki:
        endpoint: https://loki-gateway.observability.svc.cluster.local/otlp
        headers:
            X-Scope-OrgID: production
        auth:
            authenticator: bearertokenauth/backends
        sending_queue:
            enabled: true
            storage: file_storage/queue
        retry_on_failure:
            enabled: true
    otlp/tempo:
        endpoint: tempo-distributor.observability.svc.cluster.local:4317
        tls:
            ca_file: /etc/otel/tls/ca.crt
        auth:
            authenticator: bearertokenauth/backends
        sending_queue:
            enabled: true
            storage: file_storage/queue
        retry_on_failure:
            enabled: true
    otlphttp/security: # SIEM, or a separate restricted Loki tenant
        endpoint: https://security-logs.example.com/otlp
        auth:
            authenticator: bearertokenauth/backends
        sending_queue:
            enabled: true
            storage: file_storage/queue
        retry_on_failure:
            enabled: true
            max_elapsed_time: 0 # never give up on security and audit logs

service:
    extensions: [health_check, file_storage/queue, bearertokenauth/backends]
    pipelines:
        traces:
            receivers: [otlp]
            processors: [memory_limiter, k8sattributes, resourcedetection, transform/scrub, redaction, tail_sampling, batch]
            exporters: [otlp/tempo]
        metrics:
            receivers: [otlp]
            processors: [memory_limiter, k8sattributes, resourcedetection, batch]
            exporters: [otlphttp/metrics]
        logs/operational:
            receivers: [otlp]
            processors: [memory_limiter, k8sattributes, resourcedetection, transform/scrub, redaction, filter/operational-logs, batch]
            exporters: [otlphttp/loki]
        logs/security:
            receivers: [otlp]
            processors: [memory_limiter, k8sattributes, resourcedetection, transform/scrub, filter/security-logs, batch]
            exporters: [otlphttp/security]
    telemetry:
        metrics:
            level: detailed
            readers:
                - pull:
                      exporter:
                          prometheus:
                              host: ${env:MY_POD_IP}
                              port: 8888
```

- In Compose, drop `k8sattributes`, use the Compose network for TLS/mTLS or loopback, and mount tokens as Compose secrets.
- The `redaction` processor covers attribute values; it cannot reliably scrub free-text log bodies. Source-side redaction in the logging pipeline ([SECURITY.md §11.5.1](./SECURITY.md#1151-architecture-requirements)) remains mandatory. Redaction patterns produce false positives (long numeric identifiers); that is accepted as defense in depth.

</OpenTelemetryCollectorStandards>

---

<DeployingTheObservabilityStack>
## 13. Deploying the Observability Stack

### 13.1 Infrastructure as Code and GitOps

| Requirement | Mandatory Behavior |
| --- | --- |
| **Charts / manifests** | Pinned Helm charts or Kustomize bases: `open-telemetry/opentelemetry-collector`, `prometheus-community/kube-prometheus-stack` (or `prometheus`), `grafana/loki`, `grafana/tempo`, `grafana` (or Grafana Operator); `grafana/pyroscope` only if adopted. Disable bundled duplicates so there is one instance of each tool. |
| **Compose** | The single-host topology includes `otel-collector`, Prometheus, Loki, Tempo, and Grafana in `docker-compose-prod.yml` (or a companion file) under the same hardening rules as [CONTAINERS.md §5](./CONTAINERS.md#5-docker-compose-standards). |
| **GitOps** | The stack is reconciled by Argo CD or Flux from Git with per-environment overlays; manual `helm upgrade`/`kubectl apply` in production is forbidden. |
| **Object storage** | Buckets for Loki, Tempo, and metrics long-term storage are provisioned with Terraform (the `storage.tf` of each provider template — [IAC.md §5.1](./IAC.md#51-provider-templates)): encrypted with a customer-managed key, public access blocked, lifecycle rules implementing [Section 15](#15-retention-and-cost-management). |
| **Workload identity** | Backends reach object storage through workload identity; static access keys are forbidden ([IAC.md §7.2](./IAC.md#72-least-privilege-for-infrastructure)). |
| **Dedicated namespace** | Components run in the `observability` namespace with ResourceQuota, PodDisruptionBudgets, and requests/limits on every container. |
| **Isolated per environment** | Each environment has its own stack or tenants; production telemetry is never written to a non-production backend. |
| **Tagged resources** | Cloud resources carry the mandatory IaC tags. |
| **Hardening exceptions** | Platform components that cannot meet every rule of [CONTAINERS.md §3.1](./CONTAINERS.md#31-security-context) (node-level agents needing `hostPath`, `node-exporter`) are admitted only as named, documented exceptions with compensating controls (all capabilities dropped, read-only mounts, no privilege escalation), enforced by an admission policy scoped to the `observability` namespace. |

### 13.2 Network Policies

The default-deny policy of [CONTAINERS.md §7.3](./CONTAINERS.md#73-network-policies) applies. Telemetry paths are opened explicitly:

| From | To | Port |
| --- | --- | --- |
| Application Pods | Collector | TCP 4317 (and 4318 if OTLP/HTTP is used) |
| Prometheus | Application Pods | The named `metrics` port (only if the scrape endpoint is enabled) |
| Collector | Prometheus/Mimir, Loki, Tempo, security destination | Backend ingest ports |
| Grafana | Prometheus/Mimir, Loki, Tempo, Pyroscope | Backend query ports |
| Ingress controller | Grafana | HTTPS |

```yaml
# Allow application Pods in the "ecommerce" namespace to send OTLP to the Collector only
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
    name: allow-otlp-egress-to-collector
    namespace: ecommerce
spec:
    podSelector: {}
    policyTypes:
        - Egress
    egress:
        - to:
              - namespaceSelector:
                    matchLabels:
                        kubernetes.io/metadata.name: observability
                podSelector:
                    matchLabels:
                        app.kubernetes.io/name: otel-collector
          ports:
              - protocol: TCP
                port: 4317
              - protocol: TCP
                port: 4318
```

</DeployingTheObservabilityStack>

---

<TelemetrySecurityAndPrivacy>
## 14. Telemetry Security and Privacy

### 14.1 Data Minimization and Redaction

| Rule | Mandatory Behavior |
| --- | --- |
| **Classification** | Telemetry is classified Internal ([SECURITY.md §5.4](./SECURITY.md#54-data-classification-and-encryption-at-rest)); security and audit logs and profiles are Confidential. |
| **Never in any signal** | The list in [SECURITY.md §11.2](./SECURITY.md#112-what-never-to-log) applies to log fields, span attributes and events, metric labels, baggage, and profile labels — not only log messages. |
| **Source-side redaction first** | Redaction happens in the process through `Microsoft.Extensions.Compliance.Redaction` and data-classification attributes ([SECURITY.md §14.1](./SECURITY.md#141-authentication-authorization-and-web-protections)). Collector scrubbing ([Section 12.3](#123-collector-configuration)) is defense in depth, never the only control. |
| **Pseudonymous identifiers** | People are identified only by opaque IDs. Names, e-mail addresses, phone numbers, addresses, and document numbers (CPF, CNPJ) never enter telemetry. |
| **Leaked personal data is an incident** | Handled under [SECURITY.md §12](./SECURITY.md#12-incident-response): fix the source, purge the data with the backend deletion APIs, record the purge. |
| **Right to erasure** | Telemetry carries only pseudonymous identifiers, so erasure requests ([DATABASE.md §9](./DATABASE.md#9-data-governance-and-compliance)) are satisfied by retention expiry; retention periods are disclosed in the privacy documentation. |

### 14.2 Access Control and Tenancy

| Rule | Mandatory Behavior |
| --- | --- |
| **Single sign-on** | Grafana and every observability UI authenticate through an OIDC identity provider; local accounts are disabled except an audited break-glass account stored in the secrets manager. |
| **Role-based access** | Viewer by default; editors per team in non-production; admin limited to maintainers. |
| **Tenancy** | At least one tenant per environment. Security and audit logs are stored only in the security destination with restricted access ([SECURITY.md §11.3](./SECURITY.md#113-log-format-and-integrity)). |
| **Tenant set by the Collector** | The tenant header (`X-Scope-OrgID`) is set by the Collector, never by the application. |
| **No public backends** | Backend APIs (Prometheus/Mimir, Loki, Tempo, Alertmanager) are never exposed outside the cluster or host network; only Grafana (behind SSO) is reachable through the ingress. |

### 14.3 Transport, Integrity, and Untrusted Input

| Rule | Mandatory Behavior |
| --- | --- |
| **Encryption in transit** | Every telemetry hop uses TLS 1.2+ (1.3 preferred); application/agent-to-Collector uses mTLS on Kubernetes. |
| **Encryption at rest** | Object storage and persistent volumes of the stack are encrypted with customer-managed keys. |
| **Telemetry is untrusted input** | Receivers enforce message-size and rate limits. Dashboards treat attribute values as untrusted text (no HTML rendering of telemetry values in custom panels). |
| **Log injection** | Structured encoding prevents CR/LF injection; user input is passed only as template parameters, never concatenated into the message template ([SECURITY.md §2.1](./SECURITY.md#21-owasp-top-102025-web-applications), A09). |
| **Tamper detection** | Changes to retention settings, deletion API calls, and tenant limits are audited and alerted on. |

</TelemetrySecurityAndPrivacy>

---

<RetentionAndCostManagement>
## 15. Retention and Cost Management

Default retention periods; a longer period required by regulation or contract overrides them; a shorter one requires an exception.

| Signal | Queryable (hot) | Long-term |
| --- | --- | --- |
| **Application logs** | 30 days | 90 days in low-cost object storage |
| **Security and audit logs** | Per destination policy | At least 12 months, append-only ([SECURITY.md §11.3](./SECURITY.md#113-log-format-and-integrity)); the PostgreSQL audit table follows the legal retention of the audited records |
| **Metrics** | 15 days at full resolution | 13 months downsampled, for capacity and SLO trends |
| **Traces** | 14 days | None; traces referenced by an incident are exported to the incident record |
| **Profiles** | 14 days | None |

| Control | Mandatory Behavior |
| --- | --- |
| **Budget** | A budget for log ingest (GB/day), active series, and spans per second is set at onboarding and visible on a usage dashboard. |
| **Backend limits** | Ingestion-rate, series, and label limits are configured in the backends so one noisy component cannot starve others. |
| **Unused telemetry review** | Quarterly, metrics not used by any dashboard, rule, or SLO are dropped at the source or the Collector; noisy log events are reduced at the source. |
| **Cost levers, in order** | Remove debug logging, fix cardinality, tune tail-sampling baselines, reduce histogram boundaries, and only then shorten retention. |

</RetentionAndCostManagement>

---

<DotNetInstrumentation>
## 16. .NET Instrumentation

### 16.1 Packages and Bootstrap

| Requirement | Default |
| --- | --- |
| **SDK and exporter** | `OpenTelemetry.Extensions.Hosting` + `OpenTelemetry.Exporter.OpenTelemetryProtocol` |
| **Inbound/outbound HTTP** | `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http` |
| **PostgreSQL** | `Npgsql.OpenTelemetry` (`AddNpgsql()`); `OpenTelemetry.Instrumentation.EntityFrameworkCore` only if a stable version is available and its statements are sanitized |
| **Valkey** | `OpenTelemetry.Instrumentation.StackExchangeRedis` (Valkey is wire-compatible) |
| **RabbitMQ** | The client's built-in `ActivitySource`s where provided (RabbitMQ.Client 7.1+), otherwise manual `PRODUCER`/`CONSUMER` spans in the messaging adapter following the messaging semantic conventions |
| **Runtime metrics** | `OpenTelemetry.Instrumentation.Runtime` |
| **Log correlation** | `Microsoft.Extensions.Logging` with the OpenTelemetry logging provider (`builder.Logging.AddOpenTelemetry`), `IncludeFormattedMessage` and `IncludeScopes` enabled; `LoggerMessage` source generation; a JSON console formatter emitting the canonical field names for stdout |
| **Redaction** | `Microsoft.Extensions.Compliance.Redaction` |
| **Continuous profiling (optional)** | Pyroscope .NET profiler |
| **Test exporters** | `OpenTelemetry.Exporter.InMemory` |

```csharp
// Program.cs — observability bootstrap (composition root only)
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName: "ecommerce-api")) // plus OTEL_RESOURCE_ATTRIBUTES
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
        .AddHttpClientInstrumentation()
        .AddNpgsql()
        .AddRedisInstrumentation()
        .AddSource("Ecommerce.UseCases", "Ecommerce.Messaging", "RabbitMQ.Client.Publisher", "RabbitMQ.Client.Subscriber"))
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter("Ecommerce.*", "Npgsql")
        .AddView("http.server.request.duration", new ExplicitBucketHistogramConfiguration
        {
            Boundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.2, 0.3, 0.5, 0.8, 1, 2, 5, 10], // include every SLO threshold
        }))
    .UseOtlpExporter(); // endpoint/protocol from OTEL_EXPORTER_OTLP_* variables

builder.Logging.AddOpenTelemetry(o =>
{
    o.IncludeFormattedMessage = true;
    o.IncludeScopes = true;
});
```

### 16.2 Stack-Specific Rules

- Register every custom `ActivitySource` and `Meter` name with `AddSource(...)`/`AddMeter(...)`; unregistered sources are silently dropped.
- Use `IMeterFactory` for meters so they are disposed with the host.
- Mark cache-, rate-limit-, and validation-related exceptions that are handled as normal flow so they do not set span `Error` status.
- The worker role (consumers, outbox relay, jobs) uses the same bootstrap with `service.name=ecommerce-worker` and its own `ActivitySource`s.
- Zero-code (automatic) .NET instrumentation is not used.
- `console.log`-style direct output (`Console.WriteLine`) is forbidden in application code ([CODE.md §4.3](./CODE.md#43-quality-and-maintainability-rules)).

</DotNetInstrumentation>

---

<TestingObservability>
## 17. Testing Observability

Observability is tested like any other behavior ([TESTS.md](./TESTS.md)). Unit tests use in-memory exporters, never a real Collector or backend.

| Test | Verifies | Tools |
| --- | --- | --- |
| **Instrumentation contract (unit)** | Decorators and domain-event subscribers emit the expected span names, status, attributes, and metric values. | `OpenTelemetry.Exporter.InMemory`, `MetricCollector<T>` (`Microsoft.Extensions.Diagnostics.Testing`) |
| **Redaction (unit)** | Known secrets and personal data passed to logging and span enrichers never appear in emitted output. Mandatory. | A test harness asserting on captured log and span output |
| **Propagation (integration)** | Trace context crosses HTTP, RabbitMQ, and the outbox: the consumer span shares the trace ID of, or links to, the producer. | Testcontainers for PostgreSQL and RabbitMQ plus an in-memory exporter ([TESTS.md §4.4](./TESTS.md#44-messaging-and-event-integration-tests)) |
| **Alert rules** | Rules are valid and fire (and stay silent) on representative series. | `promtool check rules`, `promtool test rules` |
| **SLO specifications** | SLO files are valid and generate the expected rules. | `sloth validate` (or the chosen tool) |
| **Collector configuration** | Every change loads in the pinned distribution. | `otelcol validate --config=<file>` |
| **Dashboards** | Dashboards follow [Section 11](#11-dashboards). | Grafana `dashboard-linter` |
| **Synthetic checks** | Critical journeys work from outside in staging and production. | Blackbox Exporter; k6 scripted checks |
| **Alert fire drills** | At least quarterly for Tier 1 journeys: an injected failure fires the expected alert and the runbook resolves it. | Fault injection in staging (Toxiproxy, stopping a container); game days |
| **Overhead under load** | Telemetry overhead and pipeline capacity hold at peak load. | Load tests per [TESTS.md §7](./TESTS.md#7-performance-and-load-testing-standards) |

</TestingObservability>

---

<LocalDevelopment>
## 18. Local Development

| Rule | Mandatory Behavior |
| --- | --- |
| **All-in-one backend** | Use the `grafana/otel-lgtm` image (Collector, Prometheus, Loki, Tempo, Grafana in one container) or the .NET Aspire Dashboard, pinned by digest. For local development and CI only. |
| **Same configuration** | The application uses the same `OTEL_*` variables as production, except the endpoint (`http://localhost:4317`), sampler (`always_on`), and transport security (plaintext on loopback). |
| **Loopback only** | Local observability ports bind to `127.0.0.1`. |
| **Console exporters** | `OTEL_TRACES_EXPORTER=console` only locally, never in a deployed environment. |
| **Part of the Compose file** | The `docker-compose-dev.yml` topology includes the all-in-one backend so every developer sees traces, metrics, and logs by default. |

```yaml
# Excerpt for docker-compose-dev.yml — local development only
services:
    otel-lgtm:
        image: grafana/otel-lgtm:<ver>@sha256:<digest>
        ports:
            - "127.0.0.1:3000:3000" # Grafana
            - "127.0.0.1:4317:4317" # OTLP gRPC
            - "127.0.0.1:4318:4318" # OTLP HTTP
```

</LocalDevelopment>

---

<OperationalReadinessAndIncidents>
## 19. Operational Readiness and Incidents

A release that adds or changes a critical journey is ready for production only when each item has evidence:

| Item | Evidence |
| --- | --- |
| **SLOs and alerts** | The journey's SLOs are defined, burn-rate alerts are deployed, and their `promtool` tests pass. |
| **Dashboards** | The journey dashboard exists and shows live data from staging. |
| **Runbooks** | Every alert links to a runbook stating symptoms, impact, diagnosis steps, mitigation, and escalation. |
| **Telemetry verified under load** | The load test confirmed telemetry overhead, dashboards, and alert behavior. |
| **Redaction verified** | Redaction tests pass and a manual review of staging telemetry found no sensitive data. |
| **On-call defined** | An owner receives pages for the journey's tier. For a single-maintainer deployment, the documented escalation path and out-of-hours expectations replace a rotation, and paging is limited to `critical` alerts. |
| **Service catalog** | The `docs/` service overview lists owner, journeys, dashboards, SLOs, and runbooks. |

Incident practices:

- Operational incidents use the P1–P4 scale of [SECURITY.md §12.1](./SECURITY.md#121-classification), expressed in SLO terms (a Tier 1 fast burn is at least P2).
- A blameless post-incident review is completed within 5 business days for every P1 and P2 incident and for any incident that consumed more than 20% of an error budget.
- Every review answers: _How long did detection take, and did an alert or a human detect it first? What telemetry was missing?_ Missing telemetry becomes a tracked corrective action with owner and due date.

</OperationalReadinessAndIncidents>

---

<DefinitionOfDone>
## 20. Observability Definition of Done

A delivery is complete from an observability perspective only when all items below are true:

1. The code is instrumented only through OpenTelemetry (`ActivitySource`/`Meter`/`ILogger`), with one mechanism per signal, pinned versions, and no vendor-specific code.
2. Every signal carries the mandatory resource attributes of [Section 3.2](#32-mandatory-resource-attributes), and context-owned signals carry `app.bounded_context`.
3. The Domain layer contains no telemetry code; use-case spans and business metrics come from decorators and domain-event subscribers.
4. Logs follow the canonical schema of [Section 4.1](#41-canonical-log-schema), ship through the path of [Section 4.3](#43-log-shipping-path), and contain `trace_id`/`span_id` whenever a span is active.
5. RED, runtime, and component metrics of [Section 5.5](#55-mandatory-metrics-by-component) exist, respect naming, unit, and cardinality rules, and are exported through exactly one path.
6. Trace context propagates across HTTP, RabbitMQ, and the outbox; the correlation ID is recorded on logs and spans.
7. The journeys affected have SLOs defined as code with multi-window burn-rate alerts.
8. Every alert has `severity` and `team` labels, a runbook, a dashboard link, and a passing `promtool` test.
9. Dashboards are provisioned from Git; no dashboard, rule, or Collector setting was changed manually in production.
10. No secret, token, or personal data appears in any signal; source-side redaction is tested and Collector scrubbing is active.
11. Telemetry export is asynchronous and bounded; an outage of the Collector or backend does not affect request handling.
12. Retention and the telemetry budget are configured, and volume stays within budget.
13. Changes to the observability stack are made through IaC/GitOps with pinned versions, least-privilege RBAC, network policies, and documented exceptions.
14. Security requirements from [SECURITY.md](./SECURITY.md) are addressed for telemetry: access control, encryption, tenancy, and routing of security and audit logs.

Any exception to these standards must be documented with owner, scope, risk, rationale, and expiration date.

</DefinitionOfDone>

---

_Last updated: September 30, 2026_
_These standards must be reviewed and updated at minimum once per year or after any significant production incident or observability-pipeline outage._

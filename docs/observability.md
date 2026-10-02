# Observability (OpenTelemetry)

How the application emits logs, metrics, and traces, following `ai/OBSERVABILITY.md`. The Collector, Prometheus, Loki,
Tempo, and Grafana stack lives in the Compose files; this page covers what the application itself does.

## Layout

| Concern                                                              | Where                                                                  |
| -------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| SDK bootstrap (resource, instrumentation, views, OTLP, JSON console) | `src/SharedKernel/Infrastructure/Telemetry/ObservabilityExtensions.cs` |
| Source and meter names, attribute keys (constants only)              | `src/SharedKernel/Telemetry/TelemetryNames.cs`                         |
| Request correlation (`X-Request-ID` = trace id)                      | `src/SharedKernel/API/Telemetry/RequestCorrelationMiddleware.cs`       |
| Per-action span enrichment                                           | `src/SharedKernel/API/Telemetry/ActionTelemetryFilter.cs`              |
| Business metrics, derived from domain events after commit            | `src/<Context>/Infrastructure/Telemetry/*MetricsSubscriber.cs`         |
| Outbox backlog gauges, relay span, trace propagation                 | `OutboxBacklogMonitor.cs`, `OutboxTracing.cs`, `OutboxRelay.cs`        |

## Rules the code enforces

- **No telemetry in Domain or Application.** KPIs come from domain events handled after commit by Infrastructure
  subscribers; spans and enrichment live in the API layer (`TelemetryAndCacheConventionTests`).
- **One naming scheme.** Every source and meter is `Ecommerce.*` and comes from `TelemetryNames`; meters come from
  `IMeterFactory` so they are disposed with the host.
- **No direct console output.** Everything goes through `ILogger`; outside Development the console is one JSON object
  per line.
- **No secrets, tokens, or PII** in logs, spans, or metric labels. Actor ids are opaque ids, never e-mails.
  `OtlpExportTests` and `ObservabilityTests` assert that passwords and tokens never appear in any exported signal.
- **Telemetry never breaks the app.** Exports are asynchronous with bounded queues; with the Collector down, requests
  are unaffected (tested).

## What is emitted

**Traces**: ASP.NET Core (route template names the span; `/health` excluded), HttpClient, Npgsql, the cache boundary
(`Ecommerce.Cache`), and the outbox relay (`Ecommerce.Messaging`, a PRODUCER span). The outbox row stores the W3C
`traceparent`/`tracestate` of the request that raised the event, and the relay span continues it, so the trace survives
the asynchronous hop.

**Metrics**: RED metrics from ASP.NET Core (`http.server.request.duration` with SLO boundaries, including 0.3 s),
runtime, Npgsql, and:

| Instrument                                                                                                                               | Meaning                                               |
| ---------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------- |
| `app.cache.requests`, `app.cache.errors`, `app.cache.operation.duration`                                                                 | Cache hits/misses/bypasses, failures, latency         |
| `app.outbox.pending`, `app.outbox.oldest_pending_age`, `app.outbox.publish.duration`                                                     | Outbox backlog, oldest unpublished age, relay latency |
| `identity.signins`, `identity.accounts.registered/deactivated`, `identity.access_level.changes`, `identity.refresh_token.reuse_detected` | Identity KPIs                                         |
| `catalog.products.created/price_changes/status_changes`                                                                                  | Catalog KPIs                                          |
| `inventory.items.opened`, `inventory.reservations.created/released`, `inventory.stock.adjustments`                                       | Inventory KPIs                                        |

**Logs**: structured, with trace correlation; formatted message and scopes included.

## Configuration

Standard `OTEL_*` variables only. Nothing is exported unless `OTEL_EXPORTER_OTLP_ENDPOINT` is set (local runs, tests).

| Variable                                                 | Meaning                                                            |
| -------------------------------------------------------- | ------------------------------------------------------------------ |
| `OTEL_EXPORTER_OTLP_ENDPOINT` / `_PROTOCOL` / `_HEADERS` | Where and how to export (headers may be secret: inject at runtime) |
| `OTEL_SERVICE_NAME`                                      | Defaults to `ecommerce-api`                                        |
| `OTEL_RESOURCE_ATTRIBUTES`                               | `deployment.environment.name`, `app.owner`, `service.version`, ... |

`service.namespace` (`ecommerce`) and a generated `service.instance.id` are always set. The Compose files already carry
the standard variables (protocol, propagators, sampler, export interval, attribute limit).

## Known limitations

- Only stable OpenTelemetry packages are used; there is no beta StackExchange.Redis instrumentation (the cache boundary
  emits its own spans).
- The JSON console is not exactly the canonical log schema of `ai/OBSERVABILITY.md`; the OTLP path is canonical.
- The SDK's dropped-items metric is not exposed yet.
- Use-case spans/metrics come from the API-layer filter, not a handler decorator (handlers share no interface).
- SLOs, burn-rate alerts, dashboards, and the Collector's tail sampling remain open tasks in `ai/TASKS.md`.

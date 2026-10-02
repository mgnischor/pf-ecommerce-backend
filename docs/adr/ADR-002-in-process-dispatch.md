# ADR-002: In-process dispatch — direct handler injection and committed-event subscribers

| Attribute           | Value                                                                                                                  |
| ------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| **ID**              | ADR-002                                                                                                                |
| **Status**          | Accepted                                                                                                               |
| **Date**            | 2026-10-02                                                                                                             |
| **Decision Makers** | Project maintainers                                                                                                    |
| **Reversibility**   | Two-way door: controllers depend on concrete handlers, so a dispatcher can be added later without touching the domain. |

## Context

`ai/ARCHITECTURE.md §9.4` requires the in-process command/query dispatch decision to be recorded. Two different things
are dispatched inside the process: **use cases** (a controller invoking an application handler) and **domain events**
(an aggregate raised something, and other code in the same process reacts).

## Decision

1. **Use cases are invoked by direct injection.** A controller takes the handler class it needs
   (`OpenInventoryItemHandler`, `AdjustStockHandler`, …). Each handler is registered with its own context as its unit of
   work by `AddUseCase<TContext, THandler>`. There is no mediator, no pipeline, no reflection-based lookup.
2. **Domain events are not dispatched through a bus.** The `SaveChanges` interceptor writes them to the context's outbox in
   the same transaction (ai/DATABASE.md §3.1) and, **after the commit**, calls every registered
   `IDomainEventSubscriber`. Subscribers do infrastructure work only (business metrics, cache invalidation), are quick,
   and a failure in one is logged and swallowed because the state change is already durable.
3. **Cross-context reactions go through the broker, never through this mechanism.** Another context learns of an event
   from the outbox relay and RabbitMQ (ADR-001), through a consumer with an inbox.
4. Cross-cutting behavior that a mediator pipeline would provide (validation, transaction, telemetry) stays where it is:
   validation at the API boundary, the transaction in the unit of work, telemetry in filters and the interceptor.

## Consequences

- No new dependency, and no commercial-license exposure (MediatR 13+).
- A handler is a plain class: trivially unit-testable and navigable by "go to definition".
- Adding a use case means a controller parameter and one registration line; there is no cross-cutting pipeline for free,
  so a behavior that must apply to every use case needs a filter or an explicit call.
- Subscribers cannot veto or roll back a change; anything that must be atomic with the state change belongs in the
  aggregate or the handler, not in a subscriber.

## Alternatives

| Option                            | Why not                                                                                                                                            |
| --------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| MediatR 13+                       | Commercial license (`ai/CODE.md §4.3`).                                                                                                            |
| Source-generated `Mediator` (MIT) | Acceptable, but adds indirection and a dependency for a few handlers per context; revisit if the number of cross-cutting pipeline behaviors grows. |
| Wolverine                         | Overlaps the outbox, inbox and schema ownership already built (ADR-001).                                                                           |

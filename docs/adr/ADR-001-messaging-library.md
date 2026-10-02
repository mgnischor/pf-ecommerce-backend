# ADR-001: Messaging library — raw `RabbitMQ.Client` with the in-house outbox relay

| Attribute           | Value                                                                                                   |
| ------------------- | ------------------------------------------------------------------------------------------------------- |
| **ID**              | ADR-001                                                                                                 |
| **Status**          | Accepted                                                                                                |
| **Date**            | 2026-10-02                                                                                              |
| **Decision Makers** | Project maintainers                                                                                     |
| **Reversibility**   | Two-way door: the broker is reached only through `IOutboxPublisher` (and, later, the consumer adapter). |

## Context

`ai/ARCHITECTURE.md §9.4` requires the messaging library to be chosen once, project-wide. Events already leave the
write transaction through a transactional outbox (`OutboxSaveChangesInterceptor`) and are published by `OutboxRelay<TContext>`,
which claims rows with `FOR UPDATE SKIP LOCKED`, continues the trace stored with the event, and records the outcome.
What was missing is the adapter that sends a row to RabbitMQ.

## Decision

Use the **raw `RabbitMQ.Client` (7.x)** behind the Domain-agnostic `IOutboxPublisher` port, with the relay we already own.

- One process-wide connection with automatic recovery; one confirmed publishing channel used by one publication at a time, with publisher confirmations
  (`ai/CODE.md §5`: confirms for the relay).
- Events go to one durable `topic` exchange, `ecommerce.events`; the routing key is `{context}.{event}` in kebab case, so the
  contract does not expose CLR type names. The exchange topology is declared in code by the publisher.
- Consumers own their queues: one durable quorum queue per consumer, bound to the events exchange, with a delivery limit and a
  dead-letter queue (`{queue}.dead`) reached through the direct exchange `ecommerce.events.dead`. The consumer host declares them in code.
- Delivery is at least once; consumers deduplicate on the message id through the inbox.
- No in-process dispatcher library is adopted here (see ADR-002); domain events are dispatched to `IDomainEventSubscriber`s by the
  `SaveChanges` pipeline.

## Consequences

- No new commercial-license exposure; the only dependency is `RabbitMQ.Client` (Apache-2.0/MPL-2.0), already named in `ai/CODE.md §5`.
- We keep full control of idempotency, tracing (`PRODUCER`/`CONSUMER` spans, `traceparent` in AMQP headers) and the
  per-context schema ownership, at the price of writing the consumer adapter, retry policy and topology ourselves.
- A message with no bound queue is dropped by the broker (publish/subscribe semantics, `mandatory` is off): a consumer must
  declare its queue before it can rely on events published afterwards.

## Alternatives

| Option                 | Why not                                                                                                                               |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| Wolverine              | Brings its own outbox and conventions that would duplicate or fight the per-context outbox, inbox and schema ownership already built. |
| Rebus / DotNetCore.CAP | Same overlap with the outbox; a second abstraction over the same few operations.                                                      |
| MassTransit 9+         | Commercial license (`ai/CODE.md §4.3`); not adopted without approval.                                                                 |

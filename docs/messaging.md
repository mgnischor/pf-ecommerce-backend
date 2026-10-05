# Messaging

How events leave the application for RabbitMQ and how other contexts consume them, following `ai/ARCHITECTURE.md §9.3`
and `ai/DATABASE.md §3.1`. Decisions: [ADR-001](adr/ADR-001-messaging-library.md) (library) and
[ADR-002](adr/ADR-002-in-process-dispatch.md) (in-process dispatch). Event contracts: [events/](events/README.md).

## Publishing

State change and event are one transaction (the outbox). `OutboxRelay<TContext>` then publishes each pending row through
`RabbitMqOutboxPublisher`, which returns only after the broker **confirmed** the message. A nack, a timeout, or a lost
connection throws, the row stays pending, and it is retried after its lease. Delivery is **at least once**.

| Element              | Value                                                                                               |
| -------------------- | --------------------------------------------------------------------------------------------------- |
| Exchange             | `ecommerce.events`, durable `topic` (`RabbitMq:Exchange`)                                           |
| Dead-letter exchange | `ecommerce.events.dead`, durable `direct`; the routing key is the consumer's queue name             |
| Routing key          | `{context}.{event-name}` in kebab case, e.g. `catalog.product-created`; never a CLR name            |
| Properties           | `message-id` = event id, `correlation-id`, `type` = routing key, persistent, `application/json`     |
| Headers              | `aggregate-id`, `aggregate-version`, `causation-id` (when present), `traceparent`/`tracestate`      |
| Unroutable messages  | Dropped by the broker (`mandatory` off): a consumer must declare its queue before relying on events |

The exchanges are declared by the connection on connect (idempotent). The connection recovers by itself after a network
failure and is never replaced while it recovers, so channels and consumers survive a broker restart.

## Consuming

`RabbitMqConsumerService` runs every registered `IMessageConsumer` (`AddRabbitMqConsumers`). For each consumer it declares
(idempotently, in code) a durable **quorum** queue named after the consumer, bound to the consumer's routing key, and a
dead-letter queue `{queue}.dead`, then consumes with a channel of its own, bounded prefetch (`RabbitMq:PrefetchCount`) and
**manual acknowledgements**: a message is acknowledged only after its handler committed.

| Outcome                                  | What happens                                                                                                                                                     |
| ---------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Handled                                  | Acknowledged. `app.messaging.consumed{app.outcome="processed"}`                                                                                                  |
| Already handled (inbox row exists)       | Acknowledged, no side effect. `outcome="duplicate"`, span tag `app.messaging.duplicate=true`                                                                     |
| Handler throws                           | After a pause that doubles per attempt (cap 30 s), a copy with `x-attempt + 1` goes to the back of the queue (broker-confirmed) and the original is acknowledged |
| Attempt reaches `RabbitMq:MaxDeliveries` | Rejected without requeue: dead-lettered to `{queue}.dead`. `outcome="poisoned"`                                                                                  |
| `PoisonMessageException`                 | Dead-lettered at once: unreadable body, missing or invalid `message-id`, a rule the message can never satisfy                                                    |
| Shutdown                                 | Consumers are cancelled first (no new deliveries), in-flight handlers get up to 15 s, then channels close; what is still unacknowledged returns to its queue     |

The broker does not count `basic.nack` requeues, which is why the host counts attempts itself; the queue's
`x-delivery-limit` remains as a net for a message that crashes the consumer. Dead-letter queues are never consumed by the
application: an operator inspects them and republishes or discards (alert on their depth).

A consumer's handler takes the `IInbox` of **its own context** and calls `TryBeginAsync(messageId, ConsumerName)` before its
side effect, in the same transaction (`AddConsumerUseCase<TContext, THandler>`). The message type belongs to the consuming
context (`JsonMessageConsumer<TMessage>`): it names only the fields it needs and ignores the rest, so contexts never share
types.

### Registered consumers

| Consumer (queue)                                | Binds to                       | Effect                                                                                                                                                                               |
| ----------------------------------------------- | ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `inventory.open-item-on-product-created`        | `catalog.product-created`      | Opens the inventory item of the new SKU (BR-INV-008); leaves an existing item alone                                                                                                  |
| `cart.sync-catalog-products`                    | `catalog.*`                    | Keeps the Cart's own view of the catalog current (BR-CRT-005); ignores events it does not use                                                                                        |
| `shipping.sync-orders`                          | `ordering.*`                   | Records whose each order is (BR-SHP-004), starts the shipment when the order is paid (BR-SHP-001) and cancels it while it is being prepared (BR-SHP-005); ignores other order events |
| `ordering.mark-shipped-on-shipment-dispatched`  | `shipping.shipment-dispatched` | Moves the order to shipped (BR-ORD-003)                                                                                                                                              |
| `ordering.mark-delivered-on-shipment-delivered` | `shipping.shipment-delivered`  | Moves the order to delivered (BR-ORD-003)                                                                                                                                            |

## Roles and configuration

The API role does not need the broker: nothing below is registered unless a flag is on. The **worker role** sets
`Outbox__Relay__Enabled=true` and `RabbitMq__ConsumersEnabled=true` (the `ecommerce-worker` service in both Compose files and the `ecommerce-worker` Deployment in `kubernetes/`, scaled by KEDA on queue depth);
the process then requires the secret `ConnectionStrings__RabbitMQ` (`amqp://user:password@host:5672`, `amqps://` outside
development) and refuses to start without it. A broker that is down at start does not stop the process: readiness reports
**degraded**, consumers keep retrying to subscribe, and events wait in the outbox (`app.outbox.pending`,
`app.outbox.oldest_pending_age`). Any number of workers may run side by side.

| Setting                       | Default                 | Meaning                                                |
| ----------------------------- | ----------------------- | ------------------------------------------------------ |
| `Outbox:Relay:Enabled`        | `false`                 | Runs the relays of every context in this process       |
| `RabbitMq:ConsumersEnabled`   | `false`                 | Runs the consumers in this process                     |
| `RabbitMq:Exchange`           | `ecommerce.events`      | Events exchange                                        |
| `RabbitMq:DeadLetterExchange` | `ecommerce.events.dead` | Dead-letter exchange                                   |
| `RabbitMq:PublishTimeout`     | `00:00:10`              | Wait for one broker confirmation                       |
| `RabbitMq:ConnectTimeout`     | `00:00:05`              | One connection attempt                                 |
| `RabbitMq:PrefetchCount`      | `10`                    | Unacknowledged messages per consumer                   |
| `RabbitMq:MaxDeliveries`      | `5`                     | Attempts before a failing message is dead-lettered     |
| `RabbitMq:RetryBackoff`       | `00:00:02`              | Pause after the first failure; doubles, capped at 30 s |

Messages of one consumer are processed one at a time, in queue order apart from retries; raise throughput by running more
workers, not by parallelism inside one.

## Not built yet

Consumers for Checkout, Ordering, Billing and Notifications (those contexts have no domain yet), a purge job for expired
inbox rows, an alert on dead-letter queue depth, and automatic compatibility checking between schema versions.

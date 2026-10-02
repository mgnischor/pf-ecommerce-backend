# Integration event contracts

One JSON Schema (draft 2020-12) per event and major version: `{routing-key}.v{n}.schema.json`. The routing key
(`catalog.product-created`) is the one `RabbitMqOutboxPublisher` publishes with, so a consumer binds to it directly.

## Envelope

Every body carries `eventId`, `aggregateId`, `aggregateVersion` and `occurredAt`. The AMQP properties repeat what a
consumer needs before parsing: `message-id` (= `eventId`, what the inbox deduplicates on), `correlation-id`, `type`
(= the routing key), and the headers `aggregate-id`, `aggregate-version`, `causation-id`, `traceparent`, `tracestate`.

## Conventions

- Properties are camelCase; enums are camelCase strings; money is `{ "amount": "25.90", "currency": "BRL" }` with a decimal
  **string** amount (`ai/API_CONTRACTS.md §3`).
- **Backward-compatible within a version:** adding an optional property is allowed; removing or renaming one, changing a
  type, or tightening a rule is not. Consumers ignore unknown properties.
- A breaking change is a new schema file (`.v2`) and a new routing key suffix, published alongside `.v1` for a transition
  window agreed with the consumers.
- `tests/Portfolio.IntegrationTests/Messaging/EventContractTests.cs` serializes every domain event the way the outbox does
  and validates it against its schema, and fails when an event has no schema or a schema has no event.

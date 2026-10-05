# Shipping — Business Rules

Rules catalog for the **Shipping** bounded context (`BR-SHP-*`), as required by `ai/BUSINESS.md §3`.
Every rule is implemented in the Domain layer (or, where it needs persistence, the Application layer), has a stable
error code, and is covered by unit tests tagged `[Trait("Rule", "BR-SHP-xxx")]`.

> **Ownership and sources.** The owner and requirement source below are placeholders to be assigned by the maintainers;
> the "Enforced in" column is the only traceability that exists today. Until a requirement or ADR is linked,
> "Source" reads `Initial domain model`.

## Model

`Shipment` is the aggregate root: the physical delivery of one paid order. It starts `Preparing`, becomes `InTransit`
when the carrier takes it (carrier, tracking code and estimated delivery date are recorded then), and ends `Delivered`,
`Failed` or `Cancelled`. `OrderReference` is Shipping's **own** record of whose each order is (order, customer, number),
kept from the Ordering events, so the context checks ownership without calling Ordering or reading its schema.

```text
Preparing ──dispatched──▶ InTransit ──delivered──▶ Delivered   (terminal)
    │                         └───────failed─────▶ Failed      (terminal)
    └──order cancelled──▶ Cancelled                            (terminal)
```

## Summary

| ID         | Name                                   | Classification   | Criticality | Enforced in                                                      |
| ---------- | -------------------------------------- | ---------------- | ----------- | ---------------------------------------------------------------- |
| BR-SHP-001 | One Shipment per Paid Order            | Invariant        | Standard    | `Shipment.Prepare`, `SyncOrderHandler`, DB unique index          |
| BR-SHP-002 | Shipment Lifecycle State Transition    | State Transition | Standard    | `Shipment`, DB check constraints                                 |
| BR-SHP-003 | Carrier Data                           | Constraint       | Standard    | `Shipment.Dispatch`, DB check constraints                        |
| BR-SHP-004 | Own Shipments Only                     | Authorization    | Standard    | `ListShipmentsHandler`, `EfShipmentRepository`, `OrderReference` |
| BR-SHP-005 | Cancel While Still Being Prepared      | State Transition | Standard    | `Shipment.Cancel`, `SyncOrderHandler`                            |
| BR-SHP-006 | Malformed Order Events Are Not Retried | Constraint       | Standard    | `SyncOrderHandler`, `OrderEventsConsumer`                        |

Common attributes for all rules: **Owner** — to be assigned; **Source** — Initial domain model;
**Effective date** — 2026-10-05 (date this catalog was written).

## BR-SHP-001 — One Shipment per Paid Order

| Attribute      | Value                                                                                                                                                                                                                                                                          |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Description    | Shipping starts a shipment when it receives `ordering.order-paid`, and an order has at most one. The same event delivered twice, or two deliveries racing, produce one shipment: the inbox deduplicates the message and the unique index `ux_shipments_order` closes the race. |
| Preconditions  | The paid event of an order. A shipment needs an order and a customer.                                                                                                                                                                                                          |
| Postconditions | The shipment is `Preparing`.                                                                                                                                                                                                                                                   |
| Error behavior | `Validation` failure `SHIPMENT_ORDER_REQUIRED`.                                                                                                                                                                                                                                |
| Implemented in | `Shipment.Prepare`, `SyncOrderHandler`, `ShipmentConfiguration`                                                                                                                                                                                                                |

## BR-SHP-002 — Shipment Lifecycle State Transition

| Attribute      | Value                                                                                                                                                                                                                                                                                                                                                              |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Description    | Allowed transitions: `Preparing → InTransit`, `InTransit → Delivered`, `InTransit → Failed` (with a reason code of 2–32 letters, digits or underscores, starting with a letter), and `Preparing → Cancelled`. Anything else is refused. Each transition records its timestamp and raises its event (`shipment-dispatched`, `-delivered`, `-failed`, `-cancelled`). |
| Preconditions  | The carrier hand-over and its outcome are recorded through Application handlers (no HTTP endpoint exposes them yet); the cancellation comes from the order.                                                                                                                                                                                                        |
| Postconditions | Status and timestamp change, the version advances, the event is in the outbox in the same transaction. Repeating the same dispatch or conclusion is recognized and answers the same result.                                                                                                                                                                        |
| Error behavior | `Conflict` failure `SHIPMENT_INVALID_STATUS_TRANSITION` (params `from`, `to`); `Validation` failure `SHIPMENT_FAILURE_REASON_INVALID`. The database also refuses a status that disagrees with its carrier data (`ck_shipments_dispatch`, `ck_shipments_carrier`).                                                                                                  |
| Implemented in | `Shipment.Dispatch/MarkDelivered/MarkFailed/Cancel`, `DispatchShipmentHandler`, `ConcludeShipmentHandler`, `ShipmentConfiguration`                                                                                                                                                                                                                                 |

## BR-SHP-003 — Carrier Data

| Attribute      | Value                                                                                                                                                                                                                                                |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | Dispatching requires a carrier name of 2 to 60 characters. The tracking code is optional: ASCII letters, digits, `-` or `_`, up to 64. The estimated delivery date is optional, a calendar date (`DateOnly`) that is not before the day of dispatch. |
| Preconditions  | Dispatching a `Preparing` shipment.                                                                                                                                                                                                                  |
| Postconditions | Carrier data is stored with the shipment and shown to the owner of the order; absent members are omitted from the response, never `null`.                                                                                                            |
| Error behavior | `Validation` failures: `SHIPMENT_CARRIER_REQUIRED`, `SHIPMENT_CARRIER_LENGTH` (params `min`, `max`), `SHIPMENT_TRACKING_CODE_INVALID` (param `max`), `SHIPMENT_ESTIMATED_DELIVERY_IN_PAST`.                                                          |
| Implemented in | `Shipment.Dispatch`, `ShipmentConfiguration`                                                                                                                                                                                                         |

## BR-SHP-004 — Own Shipments Only

| Attribute      | Value                                                                                                                                                                                                                                                                                                                                                                |
| -------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | A customer lists the shipments of their own order (`GET /orders/{id}/shipments`), keyset-paged with a signed cursor (default 20 per page). An order that is someone else's answers exactly like one that does not exist, even for staff. An order with no shipment yet answers `200` with an empty page. Ownership comes from `OrderReference`, never from Ordering. |
| Preconditions  | An authenticated customer; Shipping has already seen `ordering.order-placed` for the order.                                                                                                                                                                                                                                                                          |
| Postconditions | The listing is read-only and tracks nothing.                                                                                                                                                                                                                                                                                                                         |
| Error behavior | `NotFound`: `ORDER_NOT_FOUND`; `BadRequest` (400): `PAGE_CURSOR_INVALID`.                                                                                                                                                                                                                                                                                            |
| Implemented in | `ListShipmentsHandler`, `EfShipmentRepository`, `EfOrderReferenceRepository`, `OrderReference`                                                                                                                                                                                                                                                                       |

## BR-SHP-005 — Cancel While Still Being Prepared

| Attribute      | Value                                                                                                                                                                                                                                                                                                                 |
| -------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | When the order is cancelled (`ordering.order-cancelled`), a shipment that is still `Preparing` is cancelled. An order cancelled before it was paid has no shipment, and nothing happens. A shipment the carrier already holds cannot be cancelled: that is a conflict a person must resolve (see the open questions). |
| Preconditions  | The cancelled event of an order.                                                                                                                                                                                                                                                                                      |
| Postconditions | The shipment is `Cancelled` and `shipping.shipment-cancelled` is in the outbox.                                                                                                                                                                                                                                       |
| Error behavior | A cancellation that finds the shipment in transit is a `Conflict`; the consumer dead-letters the message instead of retrying.                                                                                                                                                                                         |
| Implemented in | `Shipment.Cancel`, `SyncOrderHandler`, `OrderEventsConsumer`                                                                                                                                                                                                                                                          |

## BR-SHP-006 — Malformed Order Events Are Not Retried

| Attribute      | Value                                                                                                                                                                                                                  |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | An order event that lacks the data its kind needs (order, customer) cannot become valid by retrying, so it is dead-lettered. Events of a kind Shipping does not use (shipped, delivered) are acknowledged and ignored. |
| Preconditions  | A delivery on `shipping.sync-orders`.                                                                                                                                                                                  |
| Postconditions | The queue is not blocked; the dead-letter queue holds the message for an operator.                                                                                                                                     |
| Error behavior | `Validation` failure `SHIPPING_ORDER_EVENT_MALFORMED`, surfaced as a poison message.                                                                                                                                   |
| Implemented in | `SyncOrderHandler`, `OrderEventsConsumer`                                                                                                                                                                              |

## Concurrency and idempotency

- A shipment carries a `version`; two concurrent changes are arbitrated by it and the loser is answered `409`.
- The consumer is idempotent per message (the inbox); placing the shipment is idempotent per order (the unique index).
- Dispatching and concluding (handlers) are idempotent for the same arguments; repeating them with different arguments is a
  conflict.

## Open questions

1. **Single shipment per order** is an assumption (see BR-ORD open questions); partial shipments would change the model.
2. **Order cancelled after dispatch.** Shipping refuses and dead-letters; there is no automatic return flow.
3. **No carrier integration and no staff endpoint.** The dispatch and conclusion handlers are complete and tested, but nothing exposes them over HTTP yet; a staff endpoint or a carrier webhook would call the
   same handlers.

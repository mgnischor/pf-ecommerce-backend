# Ordering — Business Rules

Rules catalog for the **Ordering** bounded context (`BR-ORD-*`), as required by `ai/BUSINESS.md §3`.
Every rule is implemented in the Domain layer (or, where it needs persistence, the Application layer), has a stable
error code, and is covered by unit tests tagged `[Trait("Rule", "BR-ORD-xxx")]`.

> **Ownership and sources.** The owner and requirement source below are placeholders to be assigned by the maintainers;
> the "Enforced in" column is the only traceability that exists today. Until a requirement or ADR is linked,
> "Source" reads `Initial domain model`.

## Model

`Order` is the aggregate root: the purchase a customer committed to. It holds the customer, the order number, the total
and its lines (`OrderItem`: product, SKU, name, quantity and unit price **as they were when the order was placed**).
An order is a record of what was agreed, so a later change in the catalog never changes it.

```text
AwaitingPayment ──paid──────▶ Paid ──shipped──▶ Shipped ──delivered──▶ Delivered   (terminal)
      │                         │
      └────────cancelled────────┴──▶ Cancelled                                      (terminal)
```

Ordering is moved forward by facts reported by the contexts that own them: the payment (Billing) and the shipment
(Shipping, through `shipping.shipment-dispatched` and `shipping.shipment-delivered`). It never calls them.

## Summary

| ID         | Name                              | Classification   | Criticality | Enforced in                                                 |
| ---------- | --------------------------------- | ---------------- | ----------- | ----------------------------------------------------------- |
| BR-ORD-001 | Order Content and Single Checkout | Invariant        | Financial   | `Order.Place`, `PlaceOrderHandler`, DB unique index         |
| BR-ORD-002 | Total Computed Server-Side        | Derivation       | Financial   | `Order.Place`                                               |
| BR-ORD-003 | Order Lifecycle State Transition  | State Transition | Financial   | `Order`, `AdvanceOrderHandler`, shipment consumers          |
| BR-ORD-004 | Cancellation, Idempotent          | State Transition | Financial   | `Order.Cancel`, `CancelOrderHandler`, `OrdersController`    |
| BR-ORD-005 | Unique Order Number               | Constraint       | Standard    | DB sequence, `EfOrderRepository`, DB unique index           |
| BR-ORD-006 | Own Orders, Keyset-Paged          | Authorization    | Standard    | `ListOrdersHandler`, `EfOrderRepository`, `GetOrderHandler` |

Common attributes for all rules: **Owner** — to be assigned; **Source** — Initial domain model;
**Effective date** — 2026-10-05 (date this catalog was written).

## BR-ORD-001 — Order Content and Single Checkout

| Attribute      | Value                                                                                                                                                                                                                                                                                                                        |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | An order has a customer, 1 to 50 lines, each of 1 to 99 units with a positive unit price, all priced in one currency. A checkout produces at most one order: placing it again returns the order already placed from it.                                                                                                      |
| Preconditions  | The Checkout context places the order with the lines it priced (see the open questions: no producer exists yet).                                                                                                                                                                                                             |
| Postconditions | The order is `AwaitingPayment`, has its number, and `ordering.order-placed` is in the outbox in the same transaction.                                                                                                                                                                                                        |
| Error behavior | `Validation` failures: `ORDER_NO_ITEMS`, `ORDER_TOO_MANY_ITEMS` (param `max`), `ORDER_ITEM_INVALID`, `ORDER_ITEM_QUANTITY_INVALID` (params `min`, `max`), `ORDER_CURRENCY_MIXED`, `ORDER_CUSTOMER_REQUIRED`. Two concurrent placements are closed by a unique index on the checkout, and the loser reads the winner's order. |
| Implemented in | `Order.Place`, `PlaceOrderHandler`, `OrderConfiguration`                                                                                                                                                                                                                                                                     |

## BR-ORD-002 — Total Computed Server-Side

| Attribute      | Value                                                                                                                                                                                    |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | The order total is the sum of `quantity × unit price` of its lines, computed in `decimal` when the order is placed and stored with it. No client-supplied amount exists in the contract. |
| Preconditions  | An order is being placed.                                                                                                                                                                |
| Postconditions | `Total` never changes afterwards, whatever happens to the catalog.                                                                                                                       |
| Error behavior | None of its own: the line rules of BR-ORD-001 apply.                                                                                                                                     |
| Implemented in | `Order.Place`, `OrderItem`                                                                                                                                                               |

## BR-ORD-003 — Order Lifecycle State Transition

| Attribute      | Value                                                                                                                                                                                                                                                                                                                                   |
| -------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | Allowed transitions: `AwaitingPayment → Paid`, `Paid → Shipped`, `Shipped → Delivered`, and `AwaitingPayment`/`Paid → Cancelled`. Anything else is refused. `Delivered` and `Cancelled` are terminal. Each transition records its timestamp and raises its event (`order-paid`, `order-shipped`, `order-delivered`, `order-cancelled`). |
| Preconditions  | The fact comes from the owning context: payment confirmed, shipment dispatched, shipment delivered.                                                                                                                                                                                                                                     |
| Postconditions | `Status` and the matching `*At` timestamp change, the version advances, and the event is in the outbox in the same transaction. A message delivered twice applies once (the consumer's inbox).                                                                                                                                          |
| Error behavior | `Conflict` failure `ORDER_INVALID_STATUS_TRANSITION` (params `from`, `to`). For a consumer a refused transition or an unknown order is not retriable: the message is dead-lettered for a person to decide.                                                                                                                              |
| Implemented in | `Order.MarkPaid/MarkShipped/MarkDelivered`, `AdvanceOrderHandler`, `ShipmentDispatchedConsumer`, `ShipmentDeliveredConsumer`                                                                                                                                                                                                            |

## BR-ORD-004 — Cancellation, Idempotent

| Attribute      | Value                                                                                                                                                                                                                                                                                                                                                                                                                  |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | The owner cancels an order that is `AwaitingPayment` or `Paid`, with a reason code (2–32 letters, digits or underscores, starting with a letter) and an optional note (up to 500 characters). The request carries an `Idempotency-Key` and the order's `If-Match` version. Repeating the same cancellation with the same key answers the same result and does nothing twice; the replay is checked before the version. |
| Preconditions  | The caller owns the order; the order is not yet shipped.                                                                                                                                                                                                                                                                                                                                                               |
| Postconditions | `Cancelled`, with `cancelled_at`, the reason and the key stored on the aggregate. `order-cancelled` carries `wasPaid` (so a refund can follow) and **never the free-text note**.                                                                                                                                                                                                                                       |
| Error behavior | `Validation`: `ORDER_REASON_REQUIRED`, `ORDER_REASON_INVALID`, `ORDER_NOTE_TOO_LONG`, `IDEMPOTENCY_KEY_REUSED` (the key was used for a different cancellation). `Conflict`: `ORDER_INVALID_STATUS_TRANSITION`. `PreconditionFailed` (412): `ORDER_VERSION_MISMATCH`.                                                                                                                                                   |
| Implemented in | `Order.Cancel`, `Order.WasCancelledFor`, `CancelOrderHandler`, `OrdersController`                                                                                                                                                                                                                                                                                                                                      |

## BR-ORD-005 — Unique Order Number

| Attribute      | Value                                                                                                                                                                                                                                          |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | Every order has a human-readable number `PF-{year}-{sequence:D6}`, drawn from a database sequence, so concurrent placements never collide. A number is never reused, not even after a logical deletion (the unique index covers deleted rows). |
| Preconditions  | An order is being placed. The runtime role needs `USAGE` on the sequence (granted by `database/provision-roles.sql`).                                                                                                                          |
| Postconditions | The number is stored with the order and appears in its events and in the API. Gaps in the sequence are possible and expected (a rolled-back placement burns a number).                                                                         |
| Error behavior | `Validation` failure `ORDER_NUMBER_REQUIRED` when a number is missing at the domain boundary.                                                                                                                                                  |
| Implemented in | `EfOrderRepository.NextNumberAsync`, `OrderingDbContext`, `OrderConfiguration`                                                                                                                                                                 |

## BR-ORD-006 — Own Orders, Keyset-Paged

| Attribute      | Value                                                                                                                                                                                                                                                                                                                                                                      |
| -------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | A customer lists and reads only their own orders. A collection is paginated with a signed keyset cursor (1–100 per page, default 20), newest first or by total (`sort=placedAt` or `sort=total`, `-` prefix for descending), and can be filtered by `status` and `createdFrom`. An order of another customer answers exactly like one that does not exist, even for staff. |
| Preconditions  | An authenticated customer.                                                                                                                                                                                                                                                                                                                                                 |
| Postconditions | An empty result is `200` with `[]`. The cursor is bound to the query it was issued for.                                                                                                                                                                                                                                                                                    |
| Error behavior | `BadRequest` (400): `SORT_FIELD_NOT_ALLOWED` (param `allowed`), `PAGE_CURSOR_INVALID` (malformed, forged or issued for another query). `NotFound`: `ORDER_NOT_FOUND`.                                                                                                                                                                                                      |
| Implemented in | `ListOrdersHandler`, `GetOrderHandler`, `EfOrderRepository`, `OrdersController`                                                                                                                                                                                                                                                                                            |

## Concurrency and idempotency

- An order carries a `version`; its `ETag` is that version. Cancellation requires `If-Match` and answers `412` when stale.
- Placement is idempotent per checkout (BR-ORD-001), cancellation per `Idempotency-Key` (BR-ORD-004), and the shipment
  consumers per message (the inbox).

## Open questions

1. **No producer for placement yet.** `PlaceOrderHandler` is complete and tested, but nothing calls it until the Checkout
   context exists; the `POST` for orders is deliberately not exposed. Checkout will own the call.
2. **Payment is reported by a context that does not exist yet.** `AdvanceOrderHandler` with the `Paid` milestone is the
   entry point Billing will use; until then the tests call it directly.
3. **One shipment per order** (BR-SHP-001) is an assumption: partial shipments would need a different model.
4. **Cancellation racing with dispatch.** An order cancelled after the carrier took its shipment: Shipping refuses to
   cancel the shipment and dead-letters the event (BR-SHP-005). A person decides; no automatic compensation exists.

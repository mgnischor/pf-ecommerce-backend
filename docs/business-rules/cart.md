# Cart — Business Rules

Rules catalog for the **Cart** bounded context (`BR-CRT-*`), as required by `ai/BUSINESS.md §3`.
Every rule is implemented in the Domain layer (or, where it needs persistence, the Application layer), has a stable
error code, and is covered by unit tests tagged `[Trait("Rule", "BR-CRT-xxx")]`.

> **Ownership and sources.** The owner and requirement source below are placeholders to be assigned by the maintainers;
> the "Enforced in" column is the only traceability that exists today. Until a requirement or ADR is linked,
> "Source" reads `Initial domain model`.

## Model

`ShoppingCart` is the aggregate root: one per customer in the `Active` status, holding the currency it is priced in and
its lines. `ShoppingCartItem` is a line: a product (by identifier only) and a quantity. **A line stores no price.**
`CatalogProduct` is the Cart's own view of a catalog product (SKU, name, price, whether it is sellable), kept current
from the Catalog's integration events; it is how the Cart prices lines and decides whether a product can be added,
without calling into, or sharing a type or a table with, the Catalog (anti-corruption layer).

```text
Active ──checkout──▶ CheckedOut          (terminal)
Active ──expiry────▶ Expired             (terminal)
```

## Summary

| ID         | Name                                 | Classification   | Criticality | Enforced in                                                  |
| ---------- | ------------------------------------ | ---------------- | ----------- | ------------------------------------------------------------ |
| BR-CRT-001 | One Active Cart per Customer         | Invariant        | Standard    | `OpenCartHandler` + DB partial unique index                  |
| BR-CRT-002 | Line Quantity Constraint             | Constraint       | Standard    | `ShoppingCart` + DB check constraint                         |
| BR-CRT-003 | One Line per Product, Bounded Lines  | Invariant        | Standard    | `ShoppingCart` + DB partial unique index                     |
| BR-CRT-004 | Cart Lifecycle State Transition      | State Transition | Standard    | `ShoppingCart`                                               |
| BR-CRT-005 | Server-Side Pricing from the Catalog | Derivation       | Financial   | `CatalogProduct`, `CartMapping`, `SyncCatalogProductHandler` |

Common attributes for all rules: **Owner** — to be assigned; **Source** — Initial domain model;
**Effective date** — 2026-10-05 (date this catalog was written).

## BR-CRT-001 — One Active Cart per Customer

| Attribute      | Value                                                                                                                                                                                                                                                                                                                                      |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Description    | A customer (an authenticated account) has at most one `Active` cart. Opening a cart when one is open returns that cart (`200`) instead of creating a second, which also makes a retry safe; a cart already open in another currency is a conflict. A cart that was checked out or expired does not count: the customer may open a new one. |
| Preconditions  | An authenticated caller opens a cart with an ISO 4217 currency.                                                                                                                                                                                                                                                                            |
| Postconditions | The customer has exactly one active cart, in the currency it was opened with. The currency never changes.                                                                                                                                                                                                                                  |
| Error behavior | `Validation` failure `MONEY_INVALID_CURRENCY` (field `currency`); `Conflict` failure `CART_ALREADY_OPEN` (param `currency` of the open cart). The handler check covers sequential requests; two concurrent first requests are closed by the partial unique index `ux_carts_customer_active`, and the loser is answered `409` and retries.  |
| Implemented in | `OpenCartHandler`, `ShoppingCart.Open`, `ShoppingCartConfiguration`                                                                                                                                                                                                                                                                        |

## BR-CRT-002 — Line Quantity Constraint

| Attribute      | Value                                                                                                                                                                                                                     |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | A request adds 1 to 99 units of a product, and a line never holds more than 99 units in total. Adding a product already in the cart increases its line.                                                                   |
| Preconditions  | Adding a product to an active cart.                                                                                                                                                                                       |
| Postconditions | The line holds the sum of the quantities, at most 99; the cart version advances once.                                                                                                                                     |
| Error behavior | `Validation` failure (field `quantity`): `CART_ITEM_QUANTITY_INVALID` (params `min`, `max`), `CART_LINE_QUANTITY_LIMIT` (param `max`; the line keeps its quantity). The database refuses a quantity outside 1–99 as well. |
| Implemented in | `ShoppingCart.AddItem`, `ck_cart_items_quantity`                                                                                                                                                                          |

## BR-CRT-003 — One Line per Product, Bounded Lines

| Attribute      | Value                                                                                                                                                                                                                                                            |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | A cart has at most one line per product and at most 50 distinct products. A removed line is logically deleted, so the same product can be added again as a new line; growing an existing line is allowed even when the cart is full.                             |
| Preconditions  | Adding a product to an active cart.                                                                                                                                                                                                                              |
| Postconditions | No two live lines of a cart share a product.                                                                                                                                                                                                                     |
| Error behavior | `Validation` failure `CART_ITEM_LIMIT` (field `productId`, param `max`). `NotFound` failure `CART_ITEM_NOT_FOUND` when removing a line that is not in the cart (including one already removed). Backed by the partial unique index `ux_cart_items_cart_product`. |
| Implemented in | `ShoppingCart.AddItem`, `ShoppingCart.RemoveItem`, `ShoppingCartItemConfiguration`                                                                                                                                                                               |

## BR-CRT-004 — Cart Lifecycle State Transition

| Attribute      | Value                                                                                                                                                                                                                                |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Description    | A cart is created `Active`. It can be checked out (it must have at least one line) or expire; both are terminal. Only an active cart changes: adding or removing a line on any other status is refused.                              |
| Preconditions  | `MarkCheckedOut` and `Expire` require `Active`; `MarkCheckedOut` also requires a non-empty cart.                                                                                                                                     |
| Postconditions | `Status` changes and the version advances. The Checkout context will call `MarkCheckedOut`; expiry after inactivity will be driven by a job. Both transitions exist and are tested in the domain; no endpoint or job calls them yet. |
| Error behavior | `Conflict` failures: `CART_NOT_ACTIVE` (param `status`) for a change to a cart that is not active, `CART_INVALID_STATUS_TRANSITION` (params `from`, `to`), `CART_EMPTY`. Nothing changes.                                            |
| Implemented in | `ShoppingCart.AddItem`, `RemoveItem`, `MarkCheckedOut`, `Expire`, `EnsureActive`                                                                                                                                                     |

## BR-CRT-005 — Server-Side Pricing from the Catalog

| Attribute      | Value                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Description    | Prices, line totals, and the subtotal are always computed on the server from the Cart's view of the catalog; anything price-like in a request is ignored. A product can be added only when the view says it is sellable (active, not deleted) and priced in the cart's currency. A line whose product was repriced in another currency after it was added is shown in its own currency but left out of the subtotal, which only adds amounts of the cart's currency.           |
| Preconditions  | Adding a product, or reading a cart.                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| Postconditions | The view follows the Catalog: a price change shows on the next read without touching the cart. The view is updated from `catalog.product-created`, `-price-changed`, `-status-changed`, and `-deleted`. Delivery is at least once and not ordered, so the price and the sellable flag each remember the Catalog version of the event that last set them and ignore older ones; a price, status, or deletion event that arrives before the creation is retried by the consumer. |
| Error behavior | `Validation` failures (field `productId`): `CART_PRODUCT_NOT_AVAILABLE` (unknown, not active, or deleted), `CART_CURRENCY_MISMATCH` (param `currency` of the cart). A catalog event lacking the data its kind requires is dead-lettered (`CART_CATALOG_EVENT_MALFORMED`).                                                                                                                                                                                                      |
| Implemented in | `CatalogProduct`, `CartMapping.ToView`, `AddCartItemHandler`, `SyncCatalogProductHandler`, `CatalogProductsConsumer`                                                                                                                                                                                                                                                                                                                                                           |

## Concurrency and idempotency

- A cart carries a `version`; two concurrent changes are arbitrated by it and the loser is answered `409` (it re-reads
  the cart and tries again). The `ETag` of a cart response is that version.
- Opening a cart is idempotent (BR-CRT-001). Removing a line is idempotent in effect; repeating it answers `404`.
- **Adding a product is not idempotent by nature**: repeating the request adds the units again. The contract has no
  `Idempotency-Key` for it, so a client that is unsure whether a request was applied reads the cart first.

## Ownership

Every cart belongs to the account that opened it. A cart of another customer answers exactly like one that does not
exist (`404 CART_NOT_FOUND`), for every operation, so its existence is not revealed.

## Open questions for the business owner

These behaviors are **not** specified yet; the code deliberately does not invent them.

1. **Expiry window.** After how long without activity does a cart expire, and does an expired cart keep its lines for
   the customer to recover?
2. **Stock at add time.** The Cart does not check stock when a product is added; availability is decided at checkout,
   when stock is reserved. Should adding warn the shopper when a product is out of stock?
3. **Anonymous carts.** Carts require an account. Should a visitor be able to build a cart before signing in?

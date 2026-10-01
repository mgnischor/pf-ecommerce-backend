# Inventory — Business Rules

Rules catalog for the **Inventory** bounded context (`BR-INV-*`), as required by `ai/BUSINESS.md §3`.
Every rule is implemented in the Domain layer (or, where it needs persistence, the Application layer), has a stable
error code, and is covered by unit tests tagged `[Trait("Rule", "BR-INV-xxx")]`.

> **Ownership and sources.** The owner and requirement source below are placeholders to be assigned by the maintainers;
> the "Enforced in" column is the only traceability that exists today. Until a requirement or ADR is linked,
> "Source" reads `Initial domain model`.

## Model

`InventoryItem` is the aggregate root: one per SKU, holding the physical stock (`OnHand`) and the units held by
reservations (`Reserved`). `StockMovement` is the append-only ledger entity: every manual adjustment adds one line
with the signed quantity, the normalized reason, the stock left behind, the account that recorded it, and the
client's `Idempotency-Key`. `Available` is derived, never stored.

```text
OnHand ≥ Reserved ≥ 0        Available = OnHand − Reserved
```

## Summary

| ID         | Name                            | Classification | Criticality | Enforced in                                            |
| ---------- | ------------------------------- | -------------- | ----------- | ------------------------------------------------------ |
| BR-INV-001 | Stock Level Bounds Invariant    | Invariant      | Financial   | `InventoryItem`                                        |
| BR-INV-002 | Stock Adjustment Constraint     | Constraint     | Standard    | `InventoryItem`, `StockMovement`                       |
| BR-INV-003 | Append-Only Stock Ledger        | Invariant      | Financial   | `InventoryItem`, `StockMovement`, `AdjustStockHandler` |
| BR-INV-004 | Available Stock Calculation     | Calculation    | Financial   | `InventoryItem`                                        |
| BR-INV-005 | Reservation Within Availability | Constraint     | Financial   | `InventoryItem`                                        |
| BR-INV-006 | Release Within Reservations     | Constraint     | Financial   | `InventoryItem`                                        |
| BR-INV-007 | SKU Format Constraint           | Constraint     | Standard    | `Sku`                                                  |
| BR-INV-008 | One Inventory Item per SKU      | Invariant      | Standard    | `OpenInventoryItemHandler` + DB unique index           |

Common attributes for all rules: **Owner** — to be assigned; **Source** — Initial domain model;
**Effective date** — 2026-10-01 (date this catalog was written).

## BR-INV-001 — Stock Level Bounds Invariant

| Attribute      | Value                                                                                                                                                                            |
| -------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | The physical stock never goes below the reserved quantity (so it is never negative) and never above 10 000 000 units. The upper bound keeps every sum far from integer overflow. |
| Preconditions  | Adjusting stock.                                                                                                                                                                 |
| Postconditions | `OnHand` holds the new level; nothing changes when the rule fails (no event, no movement, no version change).                                                                    |
| Error behavior | `Validation` failure on field `delta`: `STOCK_BELOW_RESERVED` (params `onHand`, `reserved`), `STOCK_ABOVE_MAXIMUM` (param `max`).                                                |
| Implemented in | `InventoryItem.Adjust`                                                                                                                                                           |

## BR-INV-002 — Stock Adjustment Constraint

| Attribute      | Value                                                                                                                                                                                                                                                             |
| -------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | An adjustment moves at least 1 and at most 100 000 units, in either direction. Its reason code is required and, after trimming and lowercasing, has 2 to 32 characters: ASCII letters, digits or `_`, starting with a letter (`stocktake`, `damage`, `return_2`). |
| Preconditions  | Adjusting stock.                                                                                                                                                                                                                                                  |
| Postconditions | The ledger stores the normalized reason code.                                                                                                                                                                                                                     |
| Error behavior | `Validation` failure: `STOCK_ADJUSTMENT_DELTA_ZERO`, `STOCK_ADJUSTMENT_DELTA_RANGE` (param `max`) on `delta`; `STOCK_REASON_REQUIRED`, `STOCK_REASON_INVALID` (params `min`, `max`) on `reasonCode`.                                                              |
| Implemented in | `InventoryItem.Adjust`, `StockMovement.NormalizeReason`                                                                                                                                                                                                           |

## BR-INV-003 — Append-Only Stock Ledger

| Attribute      | Value                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| -------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | Every successful adjustment appends exactly one `StockMovement` and raises `StockAdjusted`; a failed one appends nothing. Movements are never edited or deleted. A retry of an adjustment (same `Idempotency-Key`, same quantity and reason) answers with the item as it is and never adjusts twice; the same key used for a different adjustment is rejected. The replay check runs before the version check, because a retry carries the version its original request read. |
| Preconditions  | Adjusting stock.                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| Postconditions | The movement carries `delta`, `reasonCode`, `onHandAfter`, `recordedBy`, `idempotencyKey` and the UTC creation time.                                                                                                                                                                                                                                                                                                                                                          |
| Error behavior | `Conflict` failure `IDEMPOTENCY_KEY_REUSED` (key reused for a different adjustment). `PreconditionFailed` (412) `INVENTORY_VERSION_MISMATCH` when `If-Match` is stale or malformed.                                                                                                                                                                                                                                                                                           |
| Implemented in | `InventoryItem.Adjust`, `StockMovement`, `AdjustStockHandler`                                                                                                                                                                                                                                                                                                                                                                                                                 |

## BR-INV-004 — Available Stock Calculation

| Attribute      | Value                                                                              |
| -------------- | ---------------------------------------------------------------------------------- |
| Description    | `Available = OnHand − Reserved`. It is computed on every read and never persisted. |
| Preconditions  | Reading an item.                                                                   |
| Postconditions | `Available` is never negative (BR-INV-001).                                        |
| Error behavior | None: the calculation cannot fail.                                                 |
| Implemented in | `InventoryItem.Available`                                                          |

## BR-INV-005 — Reservation Within Availability

| Attribute      | Value                                                                                                                                                       |
| -------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | Units can be reserved only up to `Available`; the quantity is positive. A reservation raises `Reserved`, leaves `OnHand` alone, and raises `StockReserved`. |
| Preconditions  | Reserving units for an order in progress.                                                                                                                   |
| Postconditions | `Reserved` grows by the quantity; the version advances once.                                                                                                |
| Error behavior | `Validation` `STOCK_QUANTITY_MUST_BE_POSITIVE` (field `quantity`); `Conflict` `STOCK_INSUFFICIENT_AVAILABLE` (params `requested`, `available`).             |
| Implemented in | `InventoryItem.Reserve`                                                                                                                                     |

> No endpoint or handler calls `Reserve` yet: it is the domain operation the Checkout context will use through events.

## BR-INV-006 — Release Within Reservations

| Attribute      | Value                                                                                                                          |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| Description    | Units can be released only up to `Reserved`; the quantity is positive. A release lowers `Reserved` and raises `StockReleased`. |
| Preconditions  | Cancelling or expiring an order in progress.                                                                                   |
| Postconditions | `Reserved` shrinks by the quantity; the version advances once.                                                                 |
| Error behavior | `Validation` `STOCK_QUANTITY_MUST_BE_POSITIVE`; `Conflict` `STOCK_RELEASE_EXCEEDS_RESERVED` (params `requested`, `reserved`).  |
| Implemented in | `InventoryItem.Release`                                                                                                        |

## BR-INV-007 — SKU Format Constraint

| Attribute      | Value                                                                                                                                                                                                                  |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | Same format as the Catalog's published language (BR-CAT-004), restated because a context never references another context's types: trimmed, uppercase, 4 to 32 ASCII letters, digits, `-` or `_`. Unicode is rejected. |
| Preconditions  | Creating a `Sku` value.                                                                                                                                                                                                |
| Postconditions | Two SKUs that differ only by case or padding are equal.                                                                                                                                                                |
| Error behavior | `Validation` failure (field `sku`): `INVENTORY_SKU_REQUIRED`, `INVENTORY_SKU_LENGTH` (params `min`, `max`), `INVENTORY_SKU_INVALID_CHARACTERS`.                                                                        |
| Implemented in | `Sku.Create`                                                                                                                                                                                                           |

## BR-INV-008 — One Inventory Item per SKU

| Attribute      | Value                                                                                                                                                                                                                                                                                                                                                              |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Description    | At most one active (not logically deleted) inventory item exists per SKU.                                                                                                                                                                                                                                                                                          |
| Preconditions  | Opening an item.                                                                                                                                                                                                                                                                                                                                                   |
| Postconditions | The item starts with `OnHand = 0`, `Reserved = 0`, version 1, and `InventoryItemOpened` is raised.                                                                                                                                                                                                                                                                 |
| Error behavior | `Conflict` failure `INVENTORY_ITEM_ALREADY_EXISTS`. The handler check covers sequential requests and retries; concurrent requests are closed by a partial unique index (`UNIQUE (sku) WHERE deleted_at IS NULL`, `ai/DATABASE.md §3.2`) `inventory.ux_inventory_items_sku_active`, created by the first migration; a lost race surfaces as `409 DUPLICATE_RECORD`. |
| Implemented in | `OpenInventoryItemHandler` (check), `InventoryItemConfiguration` (index)                                                                                                                                                                                                                                                                                           |

## Open questions for the business owner

These behaviors are **not** specified yet; the code deliberately does not invent them.

1. **Who opens an item.** Today a manager opens it with `POST /inventory/items`, and nothing checks that the SKU
   exists in the Catalog (contexts only talk through events). Should it be opened automatically when `ProductCreated`
   arrives, and should the manual endpoint then go away?
2. **Reservation lifecycle.** `Reserve` and `Release` only move a counter. The `ai/TASKS.md` item "state machines
   for … Stock Reservation" is still open: reservation expiry, committing a reservation on shipment (which lowers
   `OnHand`), and the per-order identity of a reservation are not modeled.
3. **Reason codes.** Any well-formed code is accepted. Should there be a closed list (`stocktake`, `damage`, `theft`,
   `return`, …), and should some reasons need a higher access level?
4. **Limits.** The 100 000 per-movement and 10 000 000 on-hand ceilings are technical guards, not business figures.
5. **Low-stock alerts.** No threshold or notification exists.
6. **Retry answer.** A replayed adjustment answers with the item's current state, not a snapshot of the original response.

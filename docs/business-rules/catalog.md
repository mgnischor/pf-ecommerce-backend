# Catalog — Business Rules

Rules catalog for the **Catalog** bounded context (`BR-CAT-*`), as required by `ai/BUSINESS.md §3`.
Every rule is implemented in the Domain layer (or, where it needs persistence, the Application layer), has a stable
error code, and is covered by unit tests tagged `[Trait("Rule", "BR-CAT-xxx")]`.

> **Ownership and sources.** The owner and requirement source below are placeholders to be assigned by the maintainers;
> the "Implemented in" column is the only traceability that exists today. Until a requirement or ADR is linked,
> "Source" reads `Initial domain model`.

## Summary

| ID         | Name                                      | Classification   | Criticality | Enforced in                                      |
| ---------- | ----------------------------------------- | ---------------- | ----------- | ------------------------------------------------ |
| BR-CAT-001 | Product Naming and Description Constraint | Constraint       | Standard    | `Product`                                        |
| BR-CAT-002 | Positive Sell Price Constraint            | Constraint       | Financial   | `Product`, `Money`                               |
| BR-CAT-003 | Product Lifecycle State Transition        | State Transition | Standard    | `Product`                                        |
| BR-CAT-004 | SKU Format Constraint                     | Constraint       | Standard    | `Sku`                                            |
| BR-CAT-005 | SKU Uniqueness Constraint                 | Invariant        | Standard    | `CreateProductHandler` + DB partial unique index |
| BR-CAT-006 | Catalog Visibility and Listing            | Authorization    | Standard    | `GetProductHandler`, `ListProductsHandler`       |
| BR-CAT-007 | Logical Deletion Releases the SKU         | State Transition | Standard    | `Product`, `DeleteProductHandler`                |
| BR-CAT-008 | Idempotent Creation and Deletion          | Invariant        | Standard    | `Product`, handlers + DB unique index            |

Common attributes for all rules: **Owner** — to be assigned; **Source** — Initial domain model;
**Effective date** — 2026-09-30 (date this catalog was written; BR-CAT-001…003 are implemented since commit `d5c41f1`,
BR-CAT-004 since `bce8ee2`).

## BR-CAT-001 — Product Naming and Description Constraint

| Attribute      | Value                                                                                                                                                                                                     |
| -------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | A product name is required and, after trimming, has 3 to 200 characters. A description is optional and, after trimming, has at most 2000 characters; a blank description is stored as absent.             |
| Preconditions  | Creating a product, renaming it, or changing its description.                                                                                                                                             |
| Postconditions | The stored name and description are the trimmed values.                                                                                                                                                   |
| Error behavior | `Validation` failure (nothing changes, no event): `PRODUCT_NAME_REQUIRED` (field `name`), `PRODUCT_NAME_LENGTH` (params `min`, `max`), `PRODUCT_DESCRIPTION_TOO_LONG` (field `description`, param `max`). |
| Implemented in | `Product.Create`, `Product.Rename`, `Product.ChangeDescription`                                                                                                                                           |

## BR-CAT-002 — Positive Sell Price Constraint

| Attribute      | Value                                                                                                                                                                                 |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | The sell price of a product is greater than zero after rounding to 4 decimal places (banker's rounding, `Money`). The currency is a 3-letter ASCII ISO 4217 code.                     |
| Preconditions  | Creating a product or changing its price.                                                                                                                                             |
| Postconditions | `Price` holds the new value; a change to a different value raises `ProductPriceChanged` (old and new price). Setting the same price is a no-op: success, no event, no version change. |
| Error behavior | `Validation` failure: `PRODUCT_PRICE_MUST_BE_POSITIVE` (field `price`); an invalid currency is reported earlier as `MONEY_INVALID_CURRENCY` (field `currency`).                       |
| Implemented in | `Product.Create`, `Product.ChangePrice`, `Money.Create`                                                                                                                               |

## BR-CAT-003 — Product Lifecycle State Transition

```text
Draft ──Activate──▶ Active ──Discontinue──▶ Discontinued
```

| Attribute      | Value                                                                                                                                                                                                                                      |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Description    | A product is created as `Draft`, can be activated once, and an active product can be discontinued. `Discontinued` is terminal. No other transition exists.                                                                                 |
| Preconditions  | `Activate` requires `Draft`; `Discontinue` requires `Active`.                                                                                                                                                                              |
| Postconditions | `Status` changes and `ProductStatusChanged` (from, to) is raised.                                                                                                                                                                          |
| Error behavior | `Conflict` failure `PRODUCT_INVALID_STATUS_TRANSITION` (params `from`, `to`); nothing changes, no event. A replayed request is therefore rejected instead of applied twice; concurrent requests are arbitrated by the aggregate `version`. |
| Implemented in | `Product.Activate`, `Product.Discontinue`                                                                                                                                                                                                  |

## BR-CAT-004 — SKU Format Constraint

| Attribute      | Value                                                                                                                                                                                                                                          |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | A SKU is trimmed and normalized to uppercase, has 4 to 32 characters, and contains only ASCII letters, digits, `-` and `_`. Unicode letters and digits are rejected on purpose: visually identical but distinct codes would defeat BR-CAT-005. |
| Preconditions  | Creating a `Sku` value.                                                                                                                                                                                                                        |
| Postconditions | Two SKUs that differ only by case or padding are equal.                                                                                                                                                                                        |
| Error behavior | `Validation` failure (field `sku`): `SKU_REQUIRED`, `SKU_LENGTH` (params `min`, `max`), `SKU_INVALID_CHARACTERS`.                                                                                                                              |
| Implemented in | `Sku.Create`                                                                                                                                                                                                                                   |

## BR-CAT-005 — SKU Uniqueness Constraint

| Attribute      | Value                                                                                                                                                                                                                                                                                                                                                  |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Description    | At most one active (not logically deleted) product uses a given SKU.                                                                                                                                                                                                                                                                                   |
| Preconditions  | Creating a product.                                                                                                                                                                                                                                                                                                                                    |
| Postconditions | The product is persisted with a SKU no other active product has.                                                                                                                                                                                                                                                                                       |
| Error behavior | `Conflict` failure `PRODUCT_SKU_ALREADY_EXISTS`. The handler check covers sequential requests and retries; concurrent requests are closed by a partial unique index (`UNIQUE (sku) WHERE deleted_at IS NULL`, `ai/DATABASE.md §3.2`) `catalog.ux_products_sku_active`, created by the first migration; a lost race surfaces as `409 DUPLICATE_RECORD`. |
| Implemented in | `CreateProductHandler` (check), `ProductConfiguration` (index)                                                                                                                                                                                                                                                                                         |

## BR-CAT-006 — Catalog Visibility and Listing

| Attribute      | Value                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| -------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | Anonymous visitors and customers (below the collaborator level) see only `Active` products; catalog staff (collaborator and above) see every status. A product the caller may not see is answered exactly like one that does not exist. A listing is ordered by `name`, `price`, or `createdAt` (default `-createdAt`), the product identifier always breaks ties, and it is paginated with a signed cursor, at most 100 per page (default 20). The text search matches the name as a case-insensitive substring and the SKU exactly, and treats `%` and `_` literally. |
| Preconditions  | Reading one product or listing products.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| Postconditions | The page holds only visible, non-deleted products; following `nextCursor` neither repeats nor skips a product. A cursor is valid only for the sort and filters it was issued for.                                                                                                                                                                                                                                                                                                                                                                                       |
| Error behavior | `404 PRODUCT_NOT_FOUND` for an invisible or missing product. `BadRequest` (400): `SORT_FIELD_NOT_ALLOWED` (field `sort`), `PAGE_CURSOR_INVALID` (field `cursor`: malformed, forged, or issued for another query). A non-staff caller asking for another status gets an empty page.                                                                                                                                                                                                                                                                                      |
| Implemented in | `GetProductHandler`, `ListProductsHandler`, `EfProductRepository.BuildListQuery`, `HmacPageCursorCodec`                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |

## BR-CAT-007 — Logical Deletion Releases the SKU

| Attribute      | Value                                                                                                                                                                                                                       |
| -------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | An administrator can delete a product in any status. The deletion is logical (`deleted_at`): history is kept, the product disappears from every read, and its SKU is released for BR-CAT-005 so another product may use it. |
| Preconditions  | The product exists and the caller's `If-Match` carries its current version.                                                                                                                                                 |
| Postconditions | `DeletedAt` is set, the version advances, and `DeletionKey` holds the request's idempotency key.                                                                                                                            |
| Error behavior | `404 PRODUCT_NOT_FOUND` when the product does not exist or was deleted by another request; `412 PRODUCT_VERSION_MISMATCH` when `If-Match` is stale or malformed (nothing changes).                                          |
| Implemented in | `Product.Delete`, `DeleteProductHandler`                                                                                                                                                                                    |

## BR-CAT-008 — Idempotent Creation and Deletion

| Attribute      | Value                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | Creating and deleting a product require an `Idempotency-Key` (8–64 characters). The product stores the key of the request that created it (`creation_key`) and of the one that deleted it (`deletion_key`). A retry of a creation answers with the original product and creates nothing; a retry of a deletion answers `204` instead of `404`. The same key with a different request is rejected. Keys do not expire while the product exists. |
| Preconditions  | A request carrying an `Idempotency-Key`.                                                                                                                                                                                                                                                                                                                                                                                                       |
| Postconditions | At most one product exists per creation key (partial unique index `ux_products_creation_key`, which also covers deleted products). The replay check runs before the version check, because a retry carries the version the original read, which the original already advanced.                                                                                                                                                                 |
| Error behavior | `Validation` failure `IDEMPOTENCY_KEY_REUSED` (422): the key was used for a different creation, or its product was deleted since. A deletion retried under another key is a plain `404`.                                                                                                                                                                                                                                                       |
| Implemented in | `Product.Create`, `Product.WasCreatedFrom`, `Product.Delete`, `CreateProductHandler`, `DeleteProductHandler`                                                                                                                                                                                                                                                                                                                                   |

## Concurrency and idempotency of the other operations

Every write on an existing product carries `If-Match`; a stale or malformed value is `412 PRODUCT_VERSION_MISMATCH`.

- **Update (PATCH)** applies the name and the description as one change that advances the version once; a request that
  asks for what the product already holds succeeds without advancing it. An explicit `null` name is
  `PRODUCT_NAME_REQUIRED` (422); an explicit `null` description clears it (BR-CAT-001).
- **Change price (PUT)** with the price the product already has succeeds whatever version it carries (BR-CAT-002).
- **Activate / Discontinue** replayed are rejected: `412` with the stale version, `409` with the current one (BR-CAT-003).

## Open questions for the business owner

These behaviors are **not** specified yet; the code deliberately does not invent them.

1. **Edits on discontinued products.** Today a `Discontinued` product can still be renamed, re-described, and re-priced.
   Should `Discontinued` freeze the product (a candidate `BR-CAT-006`)?
2. **Deactivation.** There is no `Active → Draft` path (for example to pull a listing temporarily). Is `Discontinued`
   the only way out of `Active`?
3. **SKU reuse.** A logically deleted product releases its SKU. Should a `Discontinued` product keep holding its SKU?
4. **Price ceilings and currencies.** Only "greater than zero" is enforced; there is no maximum and no list of
   allowed currencies.

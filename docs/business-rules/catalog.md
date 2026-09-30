# Catalog — Business Rules

Rules catalog for the **Catalog** bounded context (`BR-CAT-*`), as required by `ai/BUSINESS.md §3`.
Every rule is implemented in the Domain layer (or, where it needs persistence, the Application layer), has a stable
error code, and is covered by unit tests tagged `[Trait("Rule", "BR-CAT-xxx")]`.

> **Ownership and sources.** The owner and requirement source below are placeholders to be assigned by the maintainers;
> the "Implemented in" column is the only traceability that exists today. Until a requirement or ADR is linked,
> "Source" reads `Initial domain model`.

## Summary

| ID         | Name                                      | Classification   | Criticality | Enforced in                                                |
| ---------- | ----------------------------------------- | ---------------- | ----------- | ---------------------------------------------------------- |
| BR-CAT-001 | Product Naming and Description Constraint | Constraint       | Standard    | `Product`                                                  |
| BR-CAT-002 | Positive Sell Price Constraint            | Constraint       | Financial   | `Product`, `Money`                                         |
| BR-CAT-003 | Product Lifecycle State Transition        | State Transition | Standard    | `Product`                                                  |
| BR-CAT-004 | SKU Format Constraint                     | Constraint       | Standard    | `Sku`                                                      |
| BR-CAT-005 | SKU Uniqueness Constraint                 | Invariant        | Standard    | `CreateProductHandler` + DB partial unique index (pending) |

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

| Attribute      | Value                                                                                                                                                                                                                                                                                                 |
| -------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Description    | At most one active (not logically deleted) product uses a given SKU.                                                                                                                                                                                                                                  |
| Preconditions  | Creating a product.                                                                                                                                                                                                                                                                                   |
| Postconditions | The product is persisted with a SKU no other active product has.                                                                                                                                                                                                                                      |
| Error behavior | `Conflict` failure `PRODUCT_SKU_ALREADY_EXISTS`. The handler check covers sequential requests and retries; concurrent requests are closed by a partial unique index (`UNIQUE (sku) WHERE deleted_at IS NULL`, `ai/DATABASE.md §3.2`) that the Infrastructure layer must add with the EF Core mapping. |
| Implemented in | `CreateProductHandler` (check), persistence mapping (pending)                                                                                                                                                                                                                                         |

## Open questions for the business owner

These behaviors are **not** specified yet; the code deliberately does not invent them.

1. **Edits on discontinued products.** Today a `Discontinued` product can still be renamed, re-described, and re-priced.
   Should `Discontinued` freeze the product (a candidate `BR-CAT-006`)?
2. **Deactivation.** There is no `Active → Draft` path (for example to pull a listing temporarily). Is `Discontinued`
   the only way out of `Active`?
3. **SKU reuse.** A logically deleted product releases its SKU. Should a `Discontinued` product keep holding its SKU?
4. **Price ceilings and currencies.** Only "greater than zero" is enforced; there is no maximum and no list of
   allowed currencies.

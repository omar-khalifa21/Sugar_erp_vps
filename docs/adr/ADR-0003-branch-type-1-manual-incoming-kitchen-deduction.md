# ADR-0003: Branch Type 1 manual incoming with optional Kitchen deduction

Date: 2026-09-27

## Decision

Branch Type 1 may post an explicit `manual_incoming.posted` event without creating a Kitchen request. The event always follows the normal ordered outbox/sync path and adds product stock at the branch. Its payload includes:

- `source: "MANUAL_BRANCH_ENTRY"`
- `deduct_kitchen_ingredients: boolean`

When the flag is false, the server does not read or validate recipes. When true, the server validates active recipes only for products in the event, calculates ingredient quantities by product and recipe IDs, and deducts those ingredients from the single active Kitchen. The sync event, branch stock projection, ingredient balances, and ingredient transaction records commit in one PostgreSQL transaction.

The event UUID is both the local document/outbox identity and the server idempotency identity. A duplicate event with the same hash returns the prior result and cannot repeat either stock effect.

## Compatibility

The payload addition is backward compatible: an absent deduction flag is treated as false. Branch Type 2 remains unchanged and cannot request Kitchen deduction through this event. Existing Kitchen-request shipment and receipt events are unchanged.

## Operational consequence

The missing-recipe behavior below was superseded by [ADR-0007](ADR-0007-deferred-manual-incoming-recipe.md).

If a deduction-enabled offline event reaches the server before a selected product has an active recipe, or before Kitchen has sufficient ingredient stock, the whole server projection remains unapplied and retryable. The Branch device retains its already committed local document and outbox entry, surfaces the server's named validation message, and retries safely after the dependency is corrected.

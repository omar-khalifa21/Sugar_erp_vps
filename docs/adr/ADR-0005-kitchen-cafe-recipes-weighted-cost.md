# ADR-0005: Kitchen recipes, cafe fulfillment and weighted ingredient cost

Status: Accepted by owner request
Date: 2026-09-23

## Decision

- Kitchen owns local offline creation of products, ingredients, recipes and cafes. Stable UUIDs are assigned before upload. Server catalog, recipe and cafe projections validate the same identities and ordered versions.
- Ingredient quantities use the item's `unit` and `quantity_scale`. The desktop converts friendly g/kg and ml/L purchase and recipe entries into that integer scale before posting.
- Kitchen stock carries an inventory cost total in minor currency units. A purchase adds its total cost; every consumption or waste removes the weighted average cost, rounded to the nearest minor unit. Historical movements keep their own immutable cost.
- A branch shipment deducts recipe ingredients when Kitchen confirms dispatch. A Kitchen cafe order deducts them when Kitchen confirms delivery. Creating an order or editing a recipe never deducts stock. Both confirmations create a durable local outbox event and immutable ingredient transaction IDs. The server verifies the recipe and cost before applying the deduction in the same transaction as the event acknowledgement.
- The server records cafe fulfillment time, event ID and production cost. A second fulfillment is rejected, while replay of the original event is recognized by the existing sync idempotency key.
- Cafes remain unavailable to Branch Type 1. Kitchen and Branch Type 2 receive cafe changes through automatic sync and bootstrap.

## Compatibility

The migration adds nullable fulfillment fields and zero-default cost columns. Existing stock with unknown historical purchase cost starts at zero valuation; future purchases establish a weighted average. Existing desktop event payloads without cost fields remain accepted at zero cost during rollout.

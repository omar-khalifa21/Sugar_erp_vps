# ADR-0004: Global catalog with site retail prices

Status: Accepted by owner request
Date: 2026-09-22

## Context

The original v1 implementation stored `retail_price_minor` on the global item. A
price edit from one branch therefore replaced the value seen by every site.
Branch-originated catalog events were also scoped to their origin site, while
Kitchen had no catalog authoring UI.

## Decision

- Item identity, SKU, Arabic name, unit, quantity scale, kind, active state and
  version remain global and retain the same UUID at every site.
- Retail selling price is stored per `(site_id, item_id)` with an immutable
  revision history. A branch only writes its own price.
- Catalog identity events are visible to every enrolled device. The optional
  `price_site_id` identifies which site a price belongs to; clients preserve
  their own price when applying another site's catalog event.
- Bootstrap resolves each branch's own price and never substitutes another
  branch's override.
- Kitchen may create and edit global catalog definitions. Its changes use the
  existing durable outbox, ordered device sequence and idempotent sync endpoint.
- The legacy global item price remains as a compatibility default while older
  clients are upgraded. Existing prices are copied to every current branch by
  migration, after which branch edits diverge independently.

## Compatibility

This is an additive Contract v1 change. Existing event fields remain valid;
`kind`, `price_site_id`, `price_version`, and bootstrap `priceVersion` are
additive. New desktop builds understand cross-site catalog routing and preserve
site-local prices.

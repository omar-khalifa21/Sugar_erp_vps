# ADR-0002: Restore Branch Type 1 item management and custom orders

Date: 2026-09-19
Status: Accepted by explicit owner instruction

## Context

Contract v1 originally made the central server the only catalog authority and disabled cafe/custom orders for Branch Type 1. On 2026-09-19, the owner explicitly requested that Branch Type 1 regain both the Items module and the existing Custom Orders module, with changes synchronized to the server and visible in Admin.

## Decision

- Branch Type 1 may create, update, archive, and remove unused catalog items locally. Catalog events are validated and projected into the central catalog; Admin reads the resulting central records.
- Branch Type 1 may use the existing customer profiles, customer-specific prices, custom orders, collections, and order status workflow.
- Branch Type 2 exposes the same Items and Custom Orders entry points while retaining its freezer/display stock rules.
- The server continues to derive the site and device profile from authenticated enrollment, validates the payload site, preserves UUIDs, sequence ordering, idempotency, immutable invoice lines, and append-only stock effects.
- Kitchen remains unable to create or manage branch custom orders. Branch Type 2 retains its existing capability.

## Compatibility

This changes a frozen authorization rule without changing the event envelope or payload schema, so existing Contract v1 clients remain wire-compatible. Older servers will reject the restored Branch Type 1 custom-order events with `WRONG_PROFILE`; therefore the matching server version must be deployed before the restored desktop module is used in production.

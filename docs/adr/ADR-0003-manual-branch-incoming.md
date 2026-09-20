# ADR-0003: Manual incoming stock at both branch profiles

Status: Accepted by owner on 20 September 2026.

The owner explicitly requires Branch Type 1 and Branch Type 2 to post incoming stock without first creating a kitchen request. This supersedes the earlier request-only/manual-incoming prohibition for this workflow.

Manual incoming is an append-only stock transaction: it requires an open shift, positive catalog quantities, and a reason; it posts once locally, enters the durable ordered outbox, and updates the VPS stock projection idempotently. Branch Type 1 posts to `SALEABLE`; Branch Type 2 posts to `FREEZER`. It does not create a kitchen shipment, receipt, ingredient deduction, or quantity-conflict record.

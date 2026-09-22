import { HttpException, Injectable } from '@nestjs/common';
import { DeviceProfile, ItemKind, Prisma, StockLocation } from '@prisma/client';
import { SyncEventDto } from './sync.dto';
import { ConfigService } from '@nestjs/config';
import { JwtService } from '@nestjs/jwt';
import { randomUUID } from 'node:crypto';

type Client = Prisma.TransactionClient;
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function error(code: string, message: string, status = 422): never {
  throw new HttpException({ code, message, retryable: status === 409 }, status);
}

function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) error('INVALID_TRANSFER', 'Transfer payload must be an object');
  return value as Record<string, unknown>;
}

function id(value: unknown): string {
  if (typeof value !== 'string' || !uuid.test(value)) error('INVALID_TRANSFER', 'Transfer identifier must be a UUID');
  return value;
}

function text(value: unknown, max = 300): string {
  if (typeof value !== 'string' || !value.trim() || value.length > max) error('INVALID_TRANSFER', 'Transfer text is missing or too long');
  return value.trim();
}

function quantity(value: unknown, allowZero = false): bigint {
  if (typeof value === 'number' && !Number.isSafeInteger(value)) error('INVALID_TRANSFER', 'Large integer quantities must use decimal strings');
  if ((typeof value !== 'string' && typeof value !== 'number') || !/^\d+$/.test(String(value)))
    error('INVALID_TRANSFER', 'Transfer quantity must be an integer');
  const parsed = BigInt(value);
  if ((!allowZero && parsed === 0n) || parsed > 9_223_372_036_854_775_807n) error('INVALID_TRANSFER', 'Transfer quantity is out of range');
  return parsed;
}

function signedQuantity(value: unknown, allowZero = false): bigint {
  if (typeof value === 'number' && !Number.isSafeInteger(value)) error('INVALID_TRANSFER', 'Large integers must use decimal strings');
  if ((typeof value !== 'string' && typeof value !== 'number') || !/^-?\d+$/.test(String(value)))
    error('INVALID_TRANSFER', 'Signed quantity must be an integer');
  const parsed = BigInt(value);
  if ((!allowZero && parsed === 0n) || parsed < -9_223_372_036_854_775_807n || parsed > 9_223_372_036_854_775_807n)
    error('INVALID_TRANSFER', 'Signed quantity is out of range');
  return parsed;
}

function lines(value: unknown): Record<string, unknown>[] {
  if (!Array.isArray(value) || value.length < 1 || value.length > 100) error('INVALID_TRANSFER', 'Transfer lines are missing or too many');
  return value.map(object);
}

function inventoryCostForUse(costMinor: bigint, availableScaled: bigint, usedScaled: bigint): bigint {
  if (availableScaled <= 0n || costMinor <= 0n) return 0n;
  if (usedScaled >= availableScaled) return costMinor;
  return (costMinor * usedScaled * 2n + availableScaled) / (availableScaled * 2n);
}

@Injectable()
export class TransferProjectionService {
  private readonly jwt: JwtService;
  constructor(config: ConfigService) { this.jwt = new JwtService({ secret: config.getOrThrow<string>('JWT_SECRET') }); }
  async apply(tx: Client, event: SyncEventDto, siteId: string, profile: DeviceProfile) {
    switch (event.event_type) {
      case 'kitchen_request.submitted': return this.request(tx, event, siteId, profile);
      case 'kitchen_request.received': return this.requestReceived(tx, event, siteId, profile);
      case 'shipment.dispatched': return this.shipment(tx, event, siteId, profile);
      case 'ingredient.received': return this.ingredientMovement(tx, event, siteId, profile, 'RECEIPT');
      case 'ingredient.waste': return this.ingredientMovement(tx, event, siteId, profile, 'WASTE');
      case 'ingredient.counted': return this.ingredientCount(tx, event, siteId, profile);
      case 'incoming_receipt.accepted':
      case 'incoming_receipt.disputed': return this.receipt(tx, event, siteId, profile);
      case 'manual_incoming.posted': return this.manualIncoming(tx, event, siteId, profile);
      case 'catalog.item.updated': return this.catalogItem(tx, event, siteId, profile);
      case 'catalog.item.deleted': return this.archiveCatalogItem(tx, event, siteId, profile);
      case 'recipe.updated': return this.recipe(tx, event, profile);
      case 'cafe_customer.created': return this.cafeCustomer(tx, event, siteId);
      case 'cafe_customer.updated': return this.updateCafeCustomer(tx, event, siteId, profile);
      case 'cafe_customer.archived': return this.archiveCafeCustomer(tx, event, siteId, profile);
      case 'cafe_customer.price_list_updated': return this.cafePriceList(tx, event, siteId, profile);
      case 'branch2.inventory.posted': return this.inventory(tx, event, siteId, profile);
      case 'sale.completed': return this.sale(tx, event, siteId, profile);
      case 'sale.corrected': return this.saleCorrection(tx, event, siteId, profile);
      case 'kitchen_return.dispatched': return this.kitchenReturn(tx, event, siteId, profile);
      case 'custom_order.created': return this.cafeOrder(tx, event, siteId);
      case 'custom_customer.payment_recorded': return this.cafePayment(tx, event, siteId);
      case 'custom_order.status_changed': return this.cafeStatus(tx, event, siteId, profile);
      default: return;
    }
  }
  private async manualIncoming(tx: Client, event: SyncEventDto, siteId: string, profile: DeviceProfile) {
    if (profile === DeviceProfile.KITCHEN) error('WRONG_PROFILE', 'Manual incoming is available to branches only', 403);
    const payload = object(event.payload);
    if (id(payload.document_id) !== event.id || id(payload.site_id) !== siteId) error('WRONG_SITE', 'Manual incoming belongs to another site', 403);
    id(payload.shift_id);
    text(payload.reason, 500);
    const expectedLocation = profile === DeviceProfile.BRANCH_TYPE_2 ? StockLocation.FREEZER : StockLocation.SALEABLE;
    if (payload.location !== expectedLocation) error('INVALID_INVENTORY', 'Manual incoming location does not match the branch profile');
    const rows = lines(payload.lines);
    const itemIds = rows.map((row) => id(row.item_id));
    if (new Set(itemIds).size !== rows.length) error('INVALID_INVENTORY', 'Manual incoming repeats an item');
    const catalog = await tx.item.findMany({ where: { id: { in: itemIds }, active: true }, select: { id: true } });
    if (catalog.length !== rows.length) error('DEPENDENCY_NOT_READY', 'Manual incoming contains an unknown product', 409);
    for (const row of rows) {
      const itemId = id(row.item_id), amount = quantity(row.quantity_scaled);
      const key = { siteId, itemId, location: expectedLocation };
      await tx.stockBalance.upsert({ where: { siteId_itemId_location: key },
        create: { ...key, quantityScaled: amount, asOfAt: new Date(event.occurred_at), sourceEventId: event.id },
        update: { quantityScaled: { increment: amount }, version: { increment: 1 }, asOfAt: new Date(event.occurred_at), sourceEventId: event.id } });
    }
  }
  private async catalogItem(tx: Client, event: SyncEventDto, siteId: string, profile: DeviceProfile) {
    const payload = object(event.payload);
    if (id(payload.site_id) !== siteId) error('WRONG_SITE', 'Catalog item belongs to another site', 403);
    const itemId = id(payload.item_id), sku = text(payload.sku, 100).toUpperCase();
    const nameAr = text(payload.name_ar, 200), unit = text(payload.unit, 30);
    const scale = quantity(payload.quantity_scale), price = quantity(payload.retail_price_minor, true);
    const version = Number(payload.version);
    if (scale > 1000n || price > 2_147_483_647n || !Number.isSafeInteger(version) || version < 1 || typeof payload.active !== 'boolean')
      error('INVALID_CATALOG_ITEM', 'Catalog item values are invalid');
    const existing = await tx.item.findUnique({ where: { id: itemId } });
    if (payload.kind !== undefined && payload.kind !== ItemKind.PRODUCT && payload.kind !== ItemKind.INGREDIENT)
      error('INVALID_CATALOG_ITEM', 'Catalog item kind is invalid');
    const kind: ItemKind = payload.kind === ItemKind.INGREDIENT ? ItemKind.INGREDIENT :
      payload.kind === ItemKind.PRODUCT ? ItemKind.PRODUCT : existing?.kind ?? ItemKind.PRODUCT;
    const metadataUnchanged = !!existing && existing.sku === sku && existing.nameAr === nameAr && existing.unit === unit
      && existing.quantityScale === Number(scale) && existing.kind === kind && existing.active === payload.active;
    if (existing && version !== existing.version + 1 && !(profile !== DeviceProfile.KITCHEN && metadataUnchanged))
      error('STALE_VERSION', 'Catalog item was updated by another client', 409);
    if (!existing && version !== 1) error('DEPENDENCY_NOT_READY', 'Catalog item base version is missing', 409);
    const conflictingSku = await tx.item.findFirst({ where: { sku, id: { not: itemId } }, select: { id: true } });
    if (conflictingSku) error('SKU_ALREADY_EXISTS', 'Catalog SKU is already in use', 409);
    const canonicalVersion = existing ? existing.version + 1 : 1;
    const item = existing
      ? await tx.item.update({ where: { id: itemId }, data: { sku, nameAr, unit, quantityScale: Number(scale), kind,
        ...(profile === DeviceProfile.KITCHEN ? { retailPriceMinor: Number(price) } : {}), active: payload.active, version: canonicalVersion } })
      : await tx.item.create({ data: { id: itemId, sku, nameAr, unit, quantityScale: Number(scale),
        retailPriceMinor: profile === DeviceProfile.KITCHEN ? Number(price) : 0, kind, active: payload.active, version } });
    await tx.retailPriceRevision.create({ data: { itemId: item.id, priceMinor: item.retailPriceMinor, version: item.version, effectiveAt: new Date(event.occurred_at) } });
    if (profile !== DeviceProfile.KITCHEN) {
      const currentPrice = await tx.siteRetailPrice.findUnique({ where: { siteId_itemId: { siteId, itemId } } });
      const priceVersion = (currentPrice?.version ?? 0) + 1;
      await tx.siteRetailPrice.upsert({
        where: { siteId_itemId: { siteId, itemId } },
        create: { siteId, itemId, priceMinor: Number(price), version: priceVersion },
        update: { priceMinor: Number(price), version: priceVersion },
      });
      await tx.siteRetailPriceRevision.create({
        data: { siteId, itemId, priceMinor: Number(price), version: priceVersion, effectiveAt: new Date(event.occurred_at) },
      });
    }
  }
  private async archiveCatalogItem(tx: Client, event: SyncEventDto, siteId: string, profile: DeviceProfile) {
    if (profile === DeviceProfile.KITCHEN)
      error('WRONG_PROFILE', 'A kitchen cannot manage product catalog items from a desktop', 403);
    const payload = object(event.payload);
    if (id(payload.site_id) !== siteId) error('WRONG_SITE', 'Catalog item belongs to another site', 403);
    const itemId = id(payload.item_id), expectedVersion = Number(payload.expected_version);
    if (!Number.isSafeInteger(expectedVersion) || expectedVersion < 1) error('INVALID_CATALOG_ITEM', 'Catalog version is invalid');
    const item = await tx.item.findUnique({ where: { id: itemId } });
    if (!item) error('DEPENDENCY_NOT_READY', 'Catalog item has not reached the server', 409);
    if (item.version !== expectedVersion) error('STALE_VERSION', 'Catalog item was updated by another client', 409);
    const archived = await tx.item.update({ where: { id: itemId }, data: { active: false, version: { increment: 1 } } });
    await tx.retailPriceRevision.create({ data: { itemId, priceMinor: archived.retailPriceMinor, version: archived.version, effectiveAt: new Date(event.occurred_at) } });
  }
  private async ingredientMovement(tx: Client, event: SyncEventDto, siteId: string, profile: DeviceProfile, kind: 'RECEIPT' | 'WASTE') {
    if (profile !== DeviceProfile.KITCHEN) error('WRONG_PROFILE', 'Only the kitchen writer can post ingredient movements', 403);
    const payload = object(event.payload), rows = lines(payload.lines), itemIds = rows.map((row) => id(row.item_id));
    if (new Set(itemIds).size !== rows.length) error('INVALID_INGREDIENT_MOVEMENT', 'Ingredient movement repeats an item');
    const catalog = await tx.item.findMany({ where: { id: { in: itemIds }, kind: 'INGREDIENT', active: true } });
    if (catalog.length !== rows.length) error('INVALID_INGREDIENT_MOVEMENT', 'Movement contains an inactive or unknown ingredient');
    for (const row of rows) {
      const itemId = id(row.item_id), amount = quantity(row.quantity_scaled), key = { siteId, itemId, location: StockLocation.KITCHEN };
      const current = await tx.stockBalance.findUnique({ where: { siteId_itemId_location: key } });
      if (kind === 'WASTE' && (!current || current.quantityScaled < amount)) error('INSUFFICIENT_INGREDIENTS', 'Waste exceeds kitchen stock', 409);
      const cost = kind === 'RECEIPT' ? quantity(row.total_cost_minor ?? 0, true)
        : inventoryCostForUse(current?.inventoryCostMinor ?? 0n, current?.quantityScaled ?? 0n, amount);
      await tx.stockBalance.upsert({ where: { siteId_itemId_location: key },
        create: { ...key, quantityScaled: kind === 'RECEIPT' ? amount : 0n, inventoryCostMinor: kind === 'RECEIPT' ? cost : 0n,
          version: 1, asOfAt: new Date(event.occurred_at), sourceEventId: event.id },
        update: { quantityScaled: kind === 'RECEIPT' ? { increment: amount } : { decrement: amount },
          inventoryCostMinor: kind === 'RECEIPT' ? { increment: cost } : { decrement: cost },
          version: { increment: 1 }, asOfAt: new Date(event.occurred_at), sourceEventId: event.id } });
      await tx.ingredientStockTransaction.create({ data: { id: row.line_id ? id(row.line_id) : randomUUID(), siteId, itemId,
        sourceEventId: event.id, kind, quantityDeltaScaled: kind === 'RECEIPT' ? amount : -amount,
        costMinor: cost, reason: text(payload.reason, 500), occurredAt: new Date(event.occurred_at) } });
    }
  }
  private async ingredientCount(tx: Client, event: SyncEventDto, siteId: string, profile: DeviceProfile) {
    if (profile !== DeviceProfile.KITCHEN) error('WRONG_PROFILE', 'Only the kitchen writer can count ingredients', 403);
    const payload = object(event.payload), rows = lines(payload.lines), businessDate = text(payload.business_date, 10);
    if (!/^\d{4}-\d{2}-\d{2}$/.test(businessDate)) error('INVALID_COUNT', 'Business date is invalid');
    const ids = rows.map((row) => id(row.item_id));
    if (new Set(ids).size !== rows.length) error('INVALID_COUNT', 'Ingredient count repeats an item');
    for (const row of rows) {
      const itemId = id(row.item_id), actual = quantity(row.actual_scaled, true), waste = quantity(row.recorded_waste_scaled, true);
      const item = await tx.item.findFirst({ where: { id: itemId, kind: 'INGREDIENT', active: true } });
      if (!item) error('INVALID_COUNT', 'Count contains an inactive or unknown ingredient');
      const key = { siteId, itemId, location: StockLocation.KITCHEN };
      const current = await tx.stockBalance.findUnique({ where: { siteId_itemId_location: key } });
      const expected = current?.quantityScaled ?? 0n, unexplained = expected - actual;
      const removedCost = unexplained > 0n
        ? inventoryCostForUse(current?.inventoryCostMinor ?? 0n, expected, unexplained) : 0n;
      await tx.ingredientVariance.create({ data: { id: id(row.line_id), siteId, itemId, businessDate: new Date(`${businessDate}T00:00:00.000Z`), expectedScaled: expected,
        actualScaled: actual, recordedWasteScaled: waste, unexplainedVarianceScaled: unexplained } });
      await tx.stockBalance.upsert({ where: { siteId_itemId_location: key }, create: { ...key, quantityScaled: actual, version: 1, asOfAt: new Date(event.occurred_at), sourceEventId: event.id },
        update: { quantityScaled: actual, inventoryCostMinor: { decrement: removedCost },
          version: { increment: 1 }, asOfAt: new Date(event.occurred_at), sourceEventId: event.id } });
      await tx.ingredientStockTransaction.create({ data: { id: id(row.line_id), siteId, itemId,
        sourceEventId: event.id, kind: 'COUNT', quantityDeltaScaled: actual - expected,
        costMinor: removedCost, reason: 'End of day count', occurredAt: new Date(event.occurred_at) } });
    }
  }
  private async cafeOrder(tx: Client, event: SyncEventDto, siteId: string) {
    const p = object(event.payload), customerId = id(p.customer_id);
    if (id(p.site_id) !== siteId) error('WRONG_SITE', 'Cafe order belongs to another site', 403);
    const customer = await tx.cafeCustomer.findUnique({ where: { id: customerId } });
    if (!customer) error('DEPENDENCY_NOT_READY', 'Cafe customer has not reached the server', 409);
    const rows = lines(p.lines), itemIds = rows.map((row) => id(row.item_id));
    if (new Set(itemIds).size !== itemIds.length) error('INVALID_CAFE', 'Order repeats an item');
    const catalog = await tx.item.findMany({ where: { id: { in: itemIds }, kind: 'PRODUCT' } });
    if (catalog.length !== rows.length) error('DEPENDENCY_NOT_READY', 'Cafe order contains an unknown product', 409);
    const details = rows.map((row) => {
      const item = catalog.find((value) => value.id === row.item_id)!;
      const count = quantity(row.quantity_scaled), scale = quantity(row.quantity_scale), price = quantity(row.unit_price_minor, true), total = quantity(row.line_total_minor, true);
      if (scale !== BigInt(item.quantityScale) || price > 2_147_483_647n || (2n * count * price + scale) / (2n * scale) !== total)
        error('INVALID_CAFE', 'Cafe order line quantities and price do not reconcile');
      return { id: id(row.line_id), itemId: item.id, quantityScaled: count, quantityScale: item.quantityScale,
        unitPriceMinor: Number(price), totalMinor: total, itemNameSnapshot: text(row.item_name), skuSnapshot: item.sku, unitSnapshot: text(row.unit, 30) };
    });
    const total = quantity(p.total_minor, true);
    if (details.reduce((sum, line) => sum + line.totalMinor, 0n) !== total) error('INVALID_CAFE', 'Cafe invoice total does not reconcile');
    await tx.cafeInvoice.create({ data: { id: id(p.custom_order_id), customerId, issuingSiteId: siteId, invoiceNumber: text(p.order_number, 100),
      businessDate: new Date(event.occurred_at.slice(0, 10)), netMinor: total, occurredAt: new Date(event.occurred_at), sourceEventId: event.id,
      lines: { create: details } } });
  }
  private async cafePayment(tx: Client, event: SyncEventDto, siteId: string) {
    const p = object(event.payload), customerId = id(p.customer_id), invoiceId = id(p.source_custom_order_id);
    if (!['CASH', 'VISA'].includes(String(p.payment_method))) error('INVALID_PAYMENT', 'Cafe payment method is invalid');
    if (id(p.site_id) !== siteId) error('WRONG_SITE', 'Cafe payment belongs to another site', 403);
    const invoice = await tx.cafeInvoice.findUnique({ where: { id: invoiceId }, include: { allocations: true } });
    if (!invoice || invoice.customerId !== customerId || invoice.issuingSiteId !== siteId) error('DEPENDENCY_NOT_READY', 'Payment invoice has not reached the server', 409);
    const amount = quantity(p.amount_minor);
    const invoices = await tx.cafeInvoice.findMany({ where: { customerId, issuingSiteId: siteId, status: { not: 'REVERSED' } }, include: { allocations: true }, orderBy: [{ occurredAt: 'asc' }, { id: 'asc' }] });
    const outstanding = invoices.map((row) => ({ invoice: row, balance: row.netMinor - row.allocations.reduce((sum, allocation) => sum + allocation.amountMinor, 0n) }));
    if (amount > outstanding.reduce((sum, row) => sum + row.balance, 0n)) error('INVALID_PAYMENT', 'Payment exceeds the outstanding customer balance');
    let remaining = amount;
    const allocations: { invoiceId: string; amountMinor: bigint }[] = [];
    for (const row of outstanding) {
      const applied = remaining < row.balance ? remaining : row.balance;
      if (applied <= 0n) continue;
      allocations.push({ invoiceId: row.invoice.id, amountMinor: applied }); remaining -= applied;
      await tx.cafeInvoice.update({ where: { id: row.invoice.id }, data: { status: applied === row.balance ? 'PAID' : 'PARTIALLY_PAID' } });
    }
    await tx.cafePayment.create({ data: { id: id(p.payment_id), customerId, collectedAtSiteId: siteId, reference: `P-${id(p.payment_id)}`,
      amountMinor: amount, occurredAt: new Date(event.occurred_at), sourceEventId: event.id, allocations: { create: allocations } } });
  }
  private async cafeStatus(tx: Client, event: SyncEventDto, siteId: string, profile: DeviceProfile) {
    const p = object(event.payload), invoiceId = id(p.custom_order_id);
    if (!['CONFIRMED', 'READY', 'DELIVERED', 'CANCELLED'].includes(String(p.status)))
      error('INVALID_CAFE', 'Custom-order status transition is invalid');
    const invoice = await tx.cafeInvoice.findUnique({ where: { id: invoiceId }, include: { lines: true } });
    if (!invoice || invoice.issuingSiteId !== siteId) error('DEPENDENCY_NOT_READY', 'Cafe invoice has not reached the server', 409);
    if (invoice.status === 'REVERSED') error('ORDER_FROZEN', 'Custom order has already been cancelled', 409);
    if (p.status === 'DELIVERED' && profile === DeviceProfile.KITCHEN) {
      if (invoice.fulfillmentEventId) error('ORDER_FROZEN', 'Cafe order was already fulfilled', 409);
      const snapshots = lines(p.recipe_snapshot), ingredientRows = lines(p.ingredient_lines);
      if (snapshots.length !== invoice.lines.length) error('INVALID_RECIPE', 'Cafe recipe snapshots are incomplete');
      const expected = new Map<string, bigint>();
      for (const line of invoice.lines) {
        const snapshot = snapshots.find((value) => value.product_item_id === line.itemId);
        const recipe = await tx.recipe.findUnique({ where: { productItemId: line.itemId }, include: { components: true } });
        if (!snapshot || !recipe || !recipe.active || Number(quantity(snapshot.recipe_version)) !== recipe.version
            || quantity(snapshot.output_scaled) !== line.quantityScaled)
          error('STALE_RECIPE', 'Cafe order recipe is missing or stale', 409);
        const snapshotComponents = lines(snapshot.components);
        if (snapshotComponents.length !== recipe.components.length) error('STALE_RECIPE', 'Cafe recipe components changed', 409);
        for (const component of recipe.components) {
          const numerator = line.quantityScaled * component.quantityScaled;
          if (numerator % recipe.outputScaled !== 0n) error('RECIPE_ROUNDING', 'Cafe quantity does not align with recipe');
          const used = numerator / recipe.outputScaled;
          const declared = snapshotComponents.find((value) => value.ingredient_item_id === component.ingredientItemId);
          if (!declared || quantity(declared.quantity_scaled) !== used) error('STALE_RECIPE', 'Cafe recipe snapshot differs from saved recipe');
          expected.set(component.ingredientItemId, (expected.get(component.ingredientItemId) ?? 0n) + used);
        }
      }
      if (ingredientRows.length !== expected.size || ingredientRows.some((row) =>
        expected.get(id(row.ingredient_item_id)) !== quantity(row.quantity_scaled)))
        error('INVALID_RECIPE_CONSUMPTION', 'Cafe ingredient use differs from recipes');
      let productionCost = 0n;
      for (const row of ingredientRows) {
        const itemId = id(row.ingredient_item_id), used = quantity(row.quantity_scaled);
        const key = { siteId, itemId, location: StockLocation.KITCHEN };
        const balance = await tx.stockBalance.findUnique({ where: { siteId_itemId_location: key } });
        if (!balance || balance.quantityScaled < used) error('INSUFFICIENT_INGREDIENTS', 'Cafe production exceeds kitchen stock', 409);
        const cost = inventoryCostForUse(balance.inventoryCostMinor, balance.quantityScaled, used);
        if (quantity(row.cost_minor, true) !== cost) error('COST_OUT_OF_SYNC', 'Cafe production cost changed', 409);
        productionCost += cost;
        await tx.stockBalance.update({ where: { siteId_itemId_location: key }, data: {
          quantityScaled: { decrement: used }, inventoryCostMinor: { decrement: cost },
          version: { increment: 1 }, asOfAt: new Date(event.occurred_at), sourceEventId: event.id } });
        await tx.ingredientStockTransaction.create({ data: { id: id(row.line_id), siteId, itemId,
          sourceEventId: event.id, kind: 'CAFE_PRODUCTION', quantityDeltaScaled: -used, costMinor: cost,
          reason: invoice.invoiceNumber, occurredAt: new Date(event.occurred_at) } });
      }
      if (quantity(p.production_cost_minor, true) !== productionCost) error('COST_OUT_OF_SYNC', 'Cafe production total differs from inventory', 409);
      await tx.cafeInvoice.update({ where: { id: invoiceId }, data: {
        fulfilledAt: new Date(event.occurred_at), fulfillmentEventId: event.id, fulfillmentCostMinor: productionCost } });
    }
    if (p.status === 'CANCELLED') {
      if (invoice.fulfillmentEventId) error('ORDER_FROZEN', 'Fulfilled cafe order cannot be cancelled', 409);
      if (!Array.isArray(p.stock_lines) || p.stock_lines.length !== 0) error('INVALID_CAFE', 'Cancellation cannot deduct stock');
      await tx.cafeInvoice.update({ where: { id: invoiceId }, data: { status: 'REVERSED' } });
    }
  }

  private async sale(tx: Client, event: SyncEventDto, siteId: string, profile: DeviceProfile) {
    if (profile === DeviceProfile.KITCHEN) error('WRONG_PROFILE', 'A kitchen cannot post a branch retail sale', 403);
    const payload = object(event.payload);
    const subtotal = quantity(payload.subtotal_minor, true), discount = quantity(payload.discount_minor, true),
      tip = quantity(payload.tip_minor, true), total = quantity(payload.total_minor, true);
    if (discount > subtotal || subtotal - discount + tip !== total) error('INVALID_SALE', 'Sale totals do not reconcile');
    if (payload.shift_kind !== 'MORNING' && payload.shift_kind !== 'EVENING') error('INVALID_SALE', 'Sale shift is invalid');
    if (!['CASH', 'VISA'].includes(String(payload.payment_method)) || !['TABLE', 'TAKEAWAY'].includes(String(payload.fulfillment)))
      error('INVALID_SALE', 'Sale payment or fulfillment is invalid');
    if (typeof payload.business_date !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(payload.business_date) ||
      new Date(payload.business_date).toISOString().slice(0, 10) !== payload.business_date) error('INVALID_SALE', 'Sale business date is invalid');
    const rows = lines(payload.lines);
    const itemIds = rows.map((row) => id(row.item_id));
    if (new Set(itemIds).size !== itemIds.length) error('INVALID_SALE', 'Sale repeats an item');
    const catalog = await tx.item.findMany({ where: { id: { in: itemIds }, kind: 'PRODUCT' }, select: { id: true, quantityScale: true } });
    if (catalog.length !== itemIds.length) error('DEPENDENCY_NOT_READY', 'Sale contains an unknown product', 409);
    let lineNet = 0n, lineDiscount = 0n, lineGross = 0n;
    for (const row of rows) {
      id(row.line_id);
      const count = quantity(row.quantity_scaled), price = quantity(row.unit_price_minor, true), scale = quantity(row.quantity_scale);
      if (quantity(row.quantity_scale) !== BigInt(catalog.find((item) => item.id === row.item_id)!.quantityScale)) error('INVALID_SALE', 'Sale quantity scale differs from the catalog');
      const gross = (2n * count * price + scale) / (2n * scale);
      const allocated = quantity(row.allocated_discount_minor, true), net = quantity(row.total_minor, true);
      if (allocated > gross || gross - allocated !== net) error('INVALID_SALE', 'Sale line price, quantity, and discount do not reconcile');
      lineNet += net; lineDiscount += allocated; lineGross += gross;
    }
    if (lineGross !== subtotal || lineNet !== subtotal - discount || lineDiscount !== discount) error('INVALID_SALE', 'Sale lines do not reconcile with the receipt');
    await tx.retailSale.create({ data: { id: id(payload.sale_id), siteId, receiptNumber: text(payload.receipt_number, 100),
      businessDate: new Date(payload.business_date), shiftKind: payload.shift_kind, netMinor: subtotal - discount,
      tipMinor: tip, occurredAt: new Date(event.occurred_at), sourceEventId: event.id } });
    if (profile === DeviceProfile.BRANCH_TYPE_1) {
      for (const row of rows) {
        const itemId = id(row.item_id), sold = quantity(row.quantity_scaled);
        const key = { siteId, itemId, location: StockLocation.SALEABLE };
        const balance = await tx.stockBalance.findUnique({ where: { siteId_itemId_location: key } });
        if (!balance || balance.quantityScaled < sold) error('CENTRAL_STOCK_MISMATCH', 'Sale exceeds the synchronized branch stock', 409);
        await tx.stockBalance.update({ where: { siteId_itemId_location: key }, data: { quantityScaled: { decrement: sold },
          version: { increment: 1 }, asOfAt: new Date(event.occurred_at), sourceEventId: event.id } });
      }
    }
  }

  private async kitchenReturn(tx: Client, event: SyncEventDto, siteId: string, profile: DeviceProfile) {
    if (profile === DeviceProfile.KITCHEN) error('WRONG_PROFILE', 'A kitchen cannot dispatch a branch return', 403);
    if (profile !== DeviceProfile.BRANCH_TYPE_1) return;
    const payload = object(event.payload), rows = lines(payload.lines);
    id(payload.return_id); id(payload.shift_id); text(payload.reference, 120); text(payload.reason, 500);
    for (const row of rows) {
      id(row.line_id);
      const itemId = id(row.item_id), sent = quantity(row.sent_scaled);
      const key = { siteId, itemId, location: StockLocation.SALEABLE };
      const balance = await tx.stockBalance.findUnique({ where: { siteId_itemId_location: key } });
      if (!balance || balance.quantityScaled < sent) error('CENTRAL_STOCK_MISMATCH', 'Kitchen return exceeds the synchronized branch stock', 409);
      await tx.stockBalance.update({ where: { siteId_itemId_location: key }, data: { quantityScaled: { decrement: sent },
        version: { increment: 1 }, asOfAt: new Date(event.occurred_at), sourceEventId: event.id } });
    }
  }

  private async saleCorrection(tx: Client, event: SyncEventDto, siteId: string, profile: DeviceProfile) {
    if (profile === DeviceProfile.KITCHEN) error('WRONG_PROFILE', 'A kitchen cannot correct a retail sale', 403);
    const payload = object(event.payload), saleId = id(payload.original_sale_id), rows = lines(payload.lines);
    const sale = await tx.retailSale.findUnique({ where: { id: saleId } });
    if (!sale || sale.siteId !== siteId || !sale.sourceEventId) error('DEPENDENCY_NOT_READY', 'Original sale has not reached the server', 409);
    if (!['CASH', 'VISA'].includes(String(payload.refund_method))) error('INVALID_CORRECTION', 'Refund method is invalid');
    const source = await tx.syncEvent.findUnique({ where: { id: sale.sourceEventId } });
    if (!source) error('DEPENDENCY_NOT_READY', 'Original sale event is unavailable', 409);
    const originalRows = lines(object(source.payload).lines);
    const previous = await tx.saleCorrection.findMany({ where: { originalSaleId: saleId }, select: { lines: true } });
    const already = new Map<string, bigint>();
    for (const correction of previous) for (const row of lines(correction.lines)) {
      const lineId = id(row.original_sale_line_id);
      already.set(lineId, (already.get(lineId) ?? 0n) - signedQuantity(row.quantity_delta_scaled));
    }
    let refund = 0n;
    const normalized = rows.map((row) => {
      id(row.correction_line_id);
      const originalLineId = id(row.original_sale_line_id), itemId = id(row.item_id);
      const original = originalRows.find((value) => value.line_id === originalLineId && value.item_id === itemId);
      if (!original) error('INVALID_CORRECTION', 'Correction line does not belong to the original sale');
      const quantityDelta = signedQuantity(row.quantity_delta_scaled), amountDelta = signedQuantity(row.amount_delta_minor),
        restock = quantity(row.restock_scaled, true), scale = quantity(row.quantity_scale);
      if (quantityDelta >= 0n || amountDelta >= 0n || restock > -quantityDelta || scale !== quantity(original.quantity_scale) ||
        (already.get(originalLineId) ?? 0n) - quantityDelta > quantity(original.quantity_scaled))
        error('INVALID_CORRECTION', 'Correction exceeds the original sale quantity or has invalid stock treatment', 409);
      const disposition = String(row.disposition);
      if (!['RESTOCK', 'DISCARD'].includes(disposition) || (disposition === 'DISCARD' && restock !== 0n))
        error('INVALID_CORRECTION', 'Correction disposition is invalid');
      refund -= amountDelta;
      return { correction_line_id: id(row.correction_line_id), original_sale_line_id: originalLineId, item_id: itemId,
        quantity_delta_scaled: quantityDelta.toString(), amount_delta_minor: amountDelta.toString(), restock_scaled: restock.toString(),
        disposition, quantity_scale: scale.toString() };
    });
    if (refund !== quantity(payload.refund_minor)) error('INVALID_CORRECTION', 'Correction refund does not reconcile');
    await tx.saleCorrection.create({ data: { id: id(payload.correction_id), originalSaleId: saleId, refundMinor: refund,
      reason: text(payload.reason, 500), actor: text(payload.actor, 200), refundMethod: String(payload.refund_method),
      lines: normalized, occurredAt: new Date(event.occurred_at), sourceEventId: event.id } });
    const location = profile === DeviceProfile.BRANCH_TYPE_1 ? StockLocation.SALEABLE : StockLocation.DISPLAY;
    for (const row of normalized) if (BigInt(row.restock_scaled) > 0n) {
      const key = { siteId, itemId: row.item_id, location };
      await tx.stockBalance.upsert({ where: { siteId_itemId_location: key },
        create: { ...key, quantityScaled: BigInt(row.restock_scaled), version: 1, asOfAt: new Date(event.occurred_at), sourceEventId: event.id },
        update: { quantityScaled: { increment: BigInt(row.restock_scaled) }, version: { increment: 1 }, asOfAt: new Date(event.occurred_at), sourceEventId: event.id } });
    }
  }

  private async request(tx: Client, event: SyncEventDto, siteId: string, profile: DeviceProfile) {
    if (profile === DeviceProfile.KITCHEN) error('WRONG_PROFILE', 'A kitchen cannot create a branch request', 403);
    const payload = object(event.payload);
    const requestId = id(payload.request_id);
    const kitchen = await tx.site.findFirst({ where: { type: 'KITCHEN', active: true } });
    if (!kitchen) error('KITCHEN_ROUTE_UNAVAILABLE', 'No active kitchen is available', 409);
    const rows = lines(payload.lines);
    const itemIds = rows.map((line) => id(line.item_id));
    if (new Set(itemIds).size !== rows.length) error('INVALID_TRANSFER', 'A request cannot repeat an item');
    await tx.kitchenRequest.create({ data: {
      id: requestId, requestingSiteId: siteId, kitchenSiteId: kitchen.id,
      version: Number(payload.version) || 1, businessDate: typeof payload.business_date === 'string' ? payload.business_date : null,
      submittedAt: new Date(event.occurred_at), sourceEventId: event.id,
      lines: { create: rows.map((line) => ({
        id: id(line.line_id), itemId: id(line.item_id), requestedScaled: quantity(line.requested_scaled),
        quantityScale: Number(quantity(line.quantity_scale)), nameSnapshot: text(line.name_snapshot),
        unitSnapshot: text(line.unit_snapshot, 30),
      })) },
    } });
  }

  private async shipment(tx: Client, event: SyncEventDto, siteId: string, profile: DeviceProfile) {
    if (profile !== DeviceProfile.KITCHEN) error('WRONG_PROFILE', 'Only a kitchen can dispatch', 403);
    const payload = object(event.payload);
    const shipmentId = id(payload.shipment_id);
    const requestId = id(payload.request_id);
    const request = await tx.kitchenRequest.findUnique({ where: { id: requestId }, include: { lines: true } });
    if (!request) error('DEPENDENCY_NOT_READY', 'Branch request has not reached the server', 409);
    if (request.kitchenSiteId !== siteId || request.requestingSiteId !== id(payload.destination_site_id))
      error('INVALID_DESTINATION', 'Shipment source or destination does not match the request');
    if (request.status === 'REJECTED' || request.status === 'CANCELLED' || request.status === 'FULFILLED')
      error('REQUEST_FROZEN', 'Request cannot receive another shipment', 409);
    const rows = lines(payload.lines);
    const finalized = payload.finalized === true;
    const seen = new Set<string>();
    for (const line of rows) {
      const requestLineId = id(line.request_line_id);
      if (seen.has(requestLineId)) error('INVALID_TRANSFER', 'Shipment repeats a request line');
      seen.add(requestLineId);
      const source = request.lines.find((value) => value.id === requestLineId);
      if (!source || source.itemId !== id(line.item_id) || source.sentScaled + quantity(line.sent_scaled) > source.requestedScaled)
        error('OVER_DISPATCH', 'Shipment exceeds the remaining requested quantity');
    }
    const snapshots = lines(payload.recipe_snapshot);
    const ingredientRows = lines(payload.ingredient_lines);
    const recipes = await tx.recipe.findMany({ where: { productItemId: { in: rows.map((line) => id(line.item_id)) }, active: true }, include: { components: true } });
    if (recipes.length !== new Set(rows.map((line) => id(line.item_id))).size || snapshots.length !== rows.length)
      error('RECIPE_REQUIRED', 'Every dispatched product requires an active recipe', 409);
    const expectedIngredients = new Map<string, bigint>();
    for (const line of rows) {
      const productId = id(line.item_id), sent = quantity(line.sent_scaled), recipe = recipes.find((value) => value.productItemId === productId)!;
      const snapshot = snapshots.find((value) => id(value.product_item_id) === productId);
      if (!snapshot || Number(quantity(snapshot.recipe_version)) !== recipe.version || quantity(snapshot.output_scaled) !== sent)
        error('STALE_RECIPE', 'Dispatch recipe snapshot is missing or stale', 409);
      const components = lines(snapshot.components);
      if (components.length !== recipe.components.length) error('STALE_RECIPE', 'Recipe components changed', 409);
      for (const component of recipe.components) {
        const numerator = sent * component.quantityScaled;
        if (numerator % recipe.outputScaled !== 0n) error('RECIPE_ROUNDING', 'Dispatch quantity does not align with recipe units');
        const used = numerator / recipe.outputScaled;
        if (!components.some((value) => id(value.ingredient_item_id) === component.ingredientItemId && quantity(value.quantity_scaled) === used)) error('STALE_RECIPE', 'Recipe snapshot quantities changed', 409);
        expectedIngredients.set(component.ingredientItemId, (expectedIngredients.get(component.ingredientItemId) ?? 0n) + used);
      }
    }
    if (ingredientRows.length !== expectedIngredients.size || ingredientRows.some((row) => expectedIngredients.get(id(row.ingredient_item_id)) !== quantity(row.quantity_scaled)))
      error('INVALID_RECIPE_CONSUMPTION', 'Ingredient deduction differs from recipe snapshots');
    let productionCostMinor = 0n;
    for (const row of ingredientRows) {
      const itemId = id(row.ingredient_item_id), used = quantity(row.quantity_scaled), key = { siteId, itemId, location: StockLocation.KITCHEN };
      const balance = await tx.stockBalance.findUnique({ where: { siteId_itemId_location: key } });
      if (!balance || balance.quantityScaled < used) error('INSUFFICIENT_INGREDIENTS', 'Kitchen ingredient stock is insufficient', 409);
      const cost = inventoryCostForUse(balance.inventoryCostMinor, balance.quantityScaled, used);
      if (row.cost_minor !== undefined && quantity(row.cost_minor, true) !== cost)
        error('COST_OUT_OF_SYNC', 'Kitchen inventory cost changed; refresh before dispatch', 409);
      productionCostMinor += cost;
      await tx.stockBalance.update({ where: { siteId_itemId_location: key }, data: {
        quantityScaled: { decrement: used }, inventoryCostMinor: { decrement: cost },
        version: { increment: 1 }, asOfAt: new Date(event.occurred_at), sourceEventId: event.id } });
      await tx.ingredientStockTransaction.create({ data: { id: row.line_id ? id(row.line_id) : randomUUID(),
        siteId, itemId, sourceEventId: event.id, kind: 'PRODUCTION_CONSUMPTION',
        quantityDeltaScaled: -used, costMinor: cost, reason: text(payload.reference, 120),
        occurredAt: new Date(event.occurred_at) } });
    }
    if (payload.production_cost_minor !== undefined && quantity(payload.production_cost_minor, true) !== productionCostMinor)
      error('COST_OUT_OF_SYNC', 'Production cost does not match ingredient stock', 409);
    await tx.kitchenShipment.create({ data: {
      id: shipmentId, requestId, sourceKitchenSiteId: siteId, destinationSiteId: request.requestingSiteId,
      reference: text(payload.reference, 120), version: Number(payload.version) || 1,
      dispatchedAt: new Date(event.occurred_at), sourceEventId: event.id,
      lines: { create: rows.map((line) => ({
        id: id(line.line_id), requestLineId: id(line.request_line_id), itemId: id(line.item_id), sentScaled: quantity(line.sent_scaled),
      })) },
    } });
    for (const line of rows) {
      await tx.kitchenRequestLine.update({ where: { id: id(line.request_line_id) }, data: { sentScaled: { increment: quantity(line.sent_scaled) } } });
    }
    const dispatched = new Map(rows.map((line) => [id(line.request_line_id), quantity(line.sent_scaled)]));
    const remaining = request.lines.some((line) => line.sentScaled + (dispatched.get(line.id) ?? 0n) < line.requestedScaled);
    await tx.kitchenRequest.update({ where: { id: requestId }, data: { status: finalized || !remaining ? 'FULFILLED' : 'PARTIAL', version: { increment: 1 } } });
  }

  private async receipt(tx: Client, event: SyncEventDto, siteId: string, profile: DeviceProfile) {
    if (profile === DeviceProfile.KITCHEN) error('WRONG_PROFILE', 'A kitchen cannot receive a branch shipment', 403);
    const payload = object(event.payload);
    const shipment = await tx.kitchenShipment.findUnique({ where: { id: id(payload.shipment_id) }, include: { lines: true } });
    if (!shipment) error('DEPENDENCY_NOT_READY', 'Shipment has not reached the server', 409);
    if (shipment.destinationSiteId !== siteId) error('WRONG_SITE', 'Shipment belongs to another branch', 403);
    if (shipment.status !== 'SENT') error('SHIPMENT_FROZEN', 'Shipment has already been received', 409);
    const rows = lines(payload.lines);
    if (rows.length !== shipment.lines.length) error('INCOMPLETE_COUNT', 'Every shipment line must be counted');
    const counts = new Map(rows.map((line) => [id(line.shipment_line_id), line]));
    if (counts.size !== rows.length || shipment.lines.some((line) => !counts.has(line.id)))
      error('INCOMPLETE_COUNT', 'Counted lines do not match the shipment');
    const exact = shipment.lines.every((line) => quantity(counts.get(line.id)!.counted_scaled, true) === line.sentScaled);
    if (exact !== (event.event_type === 'incoming_receipt.accepted'))
      error('INVALID_RECEIPT', 'Receipt outcome does not match the physical counts');
    await tx.kitchenIncomingReceipt.create({ data: {
      id: id(payload.receipt_id), shipmentId: shipment.id, status: exact ? 'ACCEPTED' : 'DISPUTED',
      countedAt: new Date(event.occurred_at), sourceEventId: event.id,
      lines: { create: rows.map((line) => ({
        id: id(line.receipt_line_id), shipmentLineId: id(line.shipment_line_id),
        countedScaled: quantity(line.counted_scaled, true), confirmedScaled: exact ? quantity(line.counted_scaled, true) : null,
      })) },
    } });
    await tx.kitchenShipment.update({ where: { id: shipment.id }, data: { status: exact ? 'RECEIVED' : 'CONFLICT', version: { increment: 1 } } });
    if (exact && profile === DeviceProfile.BRANCH_TYPE_1) {
      for (const line of shipment.lines) {
        const key = { siteId, itemId: line.itemId, location: StockLocation.SALEABLE };
        await tx.stockBalance.upsert({ where: { siteId_itemId_location: key },
          create: { ...key, quantityScaled: line.sentScaled, version: 1, asOfAt: new Date(event.occurred_at), sourceEventId: event.id },
          update: { quantityScaled: { increment: line.sentScaled }, version: { increment: 1 }, asOfAt: new Date(event.occurred_at), sourceEventId: event.id } });
      }
    }
    if (!exact) {
      const conflictId = typeof payload.conflict_id === 'string' && uuid.test(payload.conflict_id)
        ? payload.conflict_id
        : event.id;
      await tx.quantityConflict.create({ data: {
        id: conflictId,
        siteId,
        reference: shipment.reference,
        status: 'OPEN',
        version: 1,
        note: 'Shipment quantities differ from the branch physical count',
        sentAt: shipment.dispatchedAt,
        countedAt: new Date(event.occurred_at),
        sourceEventId: event.id,
        lines: { create: shipment.lines.map((line) => ({
          itemId: line.itemId,
          sentScaled: line.sentScaled,
          countedScaled: quantity(counts.get(line.id)!.counted_scaled, true),
        })) },
      } });
    }
  }

  private async cafeCustomer(tx: Client, event: SyncEventDto, siteId: string) {
    const payload = object(event.payload);
    const customerId = id(payload.customer_id);
    if (payload.site_id !== undefined && id(payload.site_id) !== siteId)
      error('WRONG_SITE', 'Cafe customer origin does not match the authenticated site', 403);
    const rows = Array.isArray(payload.prices) ? payload.prices.map(object) : [];
    if (rows.length > 500) error('INVALID_CAFE', 'Cafe price list is too large');
    const itemIds = rows.map((row) => id(row.item_id));
    if (new Set(itemIds).size !== itemIds.length) error('INVALID_CAFE', 'Cafe price list repeats an item');
    const existingItems = await tx.item.findMany({ where: { id: { in: itemIds } }, select: { id: true } });
    if (existingItems.length !== itemIds.length) error('DEPENDENCY_NOT_READY', 'Cafe price list contains an unknown item', 409);
    const code = `C-${customerId.replaceAll('-', '').slice(0, 12).toUpperCase()}`;
    await tx.cafeCustomer.create({ data: {
      id: customerId,
      code,
      name: text(payload.name, 200),
      contact: typeof payload.phone === 'string' ? payload.phone.trim().slice(0, 200) : null,
      notes: [payload.kind, payload.address].filter((value) => typeof value === 'string' && value.trim()).join(' | ').slice(0, 500) || null,
      originSiteId: siteId,
      version: Number(payload.version) || 1,
      sourceEventId: event.id,
      prices: { create: rows.map((row) => ({
        itemId: id(row.item_id),
        priceMinor: Number(quantity(row.unit_price_minor, true)),
        version: Number(payload.version) || 1,
      })) },
    } });
  }

  private async updateCafeCustomer(tx: Client, event: SyncEventDto, siteId: string, profile: DeviceProfile) {
    const payload = object(event.payload), customerId = id(payload.customer_id);
    if (id(payload.site_id) !== siteId) error('WRONG_SITE', 'Cafe update belongs to another site', 403);
    const customer = await tx.cafeCustomer.findUnique({ where: { id: customerId } });
    if (!customer) error('DEPENDENCY_NOT_READY', 'Cafe has not reached the server', 409);
    if (customer.originSiteId !== siteId && profile !== DeviceProfile.KITCHEN)
      error('WRONG_SITE', 'Only the originating site or Kitchen can edit this cafe', 403);
    const version = Number(payload.version);
    if (!Number.isSafeInteger(version) || version !== customer.version + 1)
      error('STALE_VERSION', 'Cafe changed on the server', 409);
    await tx.cafeCustomer.update({ where: { id: customerId }, data: {
      name: text(payload.name, 200),
      contact: typeof payload.phone === 'string' ? payload.phone.trim().slice(0, 200) : null,
      notes: typeof payload.notes === 'string' ? payload.notes.trim().slice(0, 500) : null,
      version,
    } });
  }
  private async recipe(tx: Client, event: SyncEventDto, profile: DeviceProfile) {
    if (profile !== DeviceProfile.KITCHEN) error('WRONG_PROFILE', 'Only Kitchen can edit recipes', 403);
    const payload = object(event.payload), productId = id(payload.product_item_id);
    const outputScaled = quantity(payload.output_scaled), version = Number(payload.version);
    const components = lines(payload.components).map((row) => ({ ingredientItemId: id(row.ingredient_item_id), quantityScaled: quantity(row.quantity_scaled) }));
    if (new Set(components.map((row) => row.ingredientItemId)).size !== components.length || !Number.isSafeInteger(version) || version < 1)
      error('INVALID_RECIPE', 'Recipe components or version are invalid');
    const product = await tx.item.findFirst({ where: { id: productId, kind: ItemKind.PRODUCT, active: true } });
    const ingredients = await tx.item.findMany({ where: { id: { in: components.map((row) => row.ingredientItemId) }, kind: ItemKind.INGREDIENT, active: true } });
    if (!product || ingredients.length !== components.length) error('INVALID_RECIPE_ITEMS', 'Recipe requires one active product and active ingredients', 409);
    const existing = await tx.recipe.findUnique({ where: { productItemId: productId } });
    if (version !== (existing?.version ?? 0) + 1) error('STALE_VERSION', 'Recipe changed on the server', 409);
    if (existing) {
      await tx.recipeComponent.deleteMany({ where: { recipeId: existing.id } });
      await tx.recipe.update({ where: { id: existing.id }, data: { outputScaled, version,
        components: { create: components } } });
    } else {
      await tx.recipe.create({ data: { productItemId: productId, outputScaled, version,
        components: { create: components } } });
    }
  }

  private async requestReceived(tx: Client, event: SyncEventDto, siteId: string, profile: DeviceProfile) {
    if (profile !== DeviceProfile.KITCHEN) error('WRONG_PROFILE', 'Only the kitchen can acknowledge a branch request', 403);
    const payload = object(event.payload);
    const requestId = id(payload.request_id);
    const destinationSiteId = id(payload.destination_site_id);
    const request = await tx.kitchenRequest.findUnique({ where: { id: requestId } });
    if (!request) error('DEPENDENCY_NOT_READY', 'Branch request has not reached the server', 409);
    if (request.kitchenSiteId !== siteId || request.requestingSiteId !== destinationSiteId)
      error('WRONG_SITE', 'Request acknowledgement does not match its kitchen and branch', 403);
    if (payload.status !== 'RECEIVED') error('INVALID_TRANSFER', 'Request acknowledgement status is invalid');
    if (request.status === 'REQUESTED') {
      await tx.kitchenRequest.update({ where: { id: requestId }, data: { status: 'RECEIVED', version: { increment: 1 } } });
    }
  }

  private async archiveCafeCustomer(tx: Client, event: SyncEventDto, siteId: string, profile: DeviceProfile) {
    if (profile === DeviceProfile.KITCHEN) error('WRONG_PROFILE', 'A kitchen cannot archive branch cafe customers', 403);
    const payload = object(event.payload), customerId = id(payload.customer_id);
    if (id(payload.site_id) !== siteId) error('WRONG_SITE', 'Cafe customer origin does not match the authenticated site', 403);
    const customer = await tx.cafeCustomer.findUnique({ where: { id: customerId } });
    if (!customer) error('DEPENDENCY_NOT_READY', 'Cafe customer has not reached the server', 409);
    if (customer.originSiteId !== siteId) error('WRONG_SITE', 'Only the owning site can archive this cafe customer', 403);
    await tx.cafeCustomer.update({ where: { id: customerId }, data: { active: false } });
  }

  private async cafePriceList(tx: Client, event: SyncEventDto, siteId: string, profile: DeviceProfile) {
    if (profile === DeviceProfile.KITCHEN)
      error('WRONG_PROFILE', 'A kitchen cannot manage branch custom-order prices', 403);
    const payload = object(event.payload), customerId = id(payload.customer_id), rows = lines(payload.prices);
    const version = Number(payload.version);
    if (!Number.isSafeInteger(version) || version < 2) error('INVALID_CAFE', 'Cafe price-list version is invalid');
    const customer = await tx.cafeCustomer.findUnique({ where: { id: customerId }, include: { prices: true } });
    if (!customer) error('DEPENDENCY_NOT_READY', 'Cafe customer has not reached the server', 409);
    if (customer.originSiteId !== siteId) error('WRONG_SITE', 'Only the owning site can update this cafe customer', 403);
    const itemIds = rows.map((row) => id(row.item_id));
    if (new Set(itemIds).size !== rows.length || rows.length !== customer.prices.length ||
      customer.prices.some((price) => !itemIds.includes(price.itemId)))
      error('INVALID_CAFE', 'A price update must contain each customer item exactly once');
    const currentVersion = Math.max(0, ...customer.prices.map((price) => price.version));
    if (version !== currentVersion + 1) error('STALE_VERSION', 'Cafe price list was updated by another client', 409);
    for (const row of rows) {
      const price = customer.prices.find((value) => value.itemId === row.item_id)!;
      const amount = quantity(row.unit_price_minor, true);
      if (amount > 2_147_483_647n) error('INVALID_CAFE', 'Cafe item price is out of range');
      await tx.cafeItemPrice.update({ where: { id: price.id }, data: {
        priceMinor: Number(amount), version,
        revisions: { create: { customerId, itemId: price.itemId, priceMinor: Number(amount), version, effectiveAt: new Date(event.occurred_at) } },
      } });
    }
  }

  private async inventory(tx: Client, event: SyncEventDto, siteId: string, profile: DeviceProfile) {
    if (profile !== DeviceProfile.BRANCH_TYPE_2) error('WRONG_PROFILE', 'Location inventory events require a Branch Type 2 writer', 403);
    const payload = object(event.payload), kind = text(payload.kind, 40), referenceId = id(payload.reference_id), userId = id(payload.user_id);
    let proof: { purpose: string; user_id: string; site_id: string; device_id: string; iat: number; exp: number };
    if (typeof payload.authorization !== 'string') error('UNAUTHENTICATED', 'A signed operator authorization is required', 401);
    try { proof = this.jwt.verify<typeof proof>(payload.authorization, { ignoreExpiration: true }); }
    catch { error('UNAUTHENTICATED', 'A signed operator authorization is required', 401); }
    const origin = await tx.syncEvent.findUniqueOrThrow({ where: { id: event.id }, select: { deviceId: true, streamEpoch: true, deviceSequence: true } });
    const occurred = Math.floor(new Date(event.occurred_at).getTime() / 1000);
    if (proof.purpose !== 'desktop-operation' || proof.user_id !== userId || proof.site_id !== siteId || proof.device_id !== origin.deviceId ||
      !Number.isFinite(proof.iat) || !Number.isFinite(proof.exp) || occurred < proof.iat - 30 || occurred > proof.exp || occurred > Date.now() / 1000 + 30)
      error('UNAUTHENTICATED', 'Operator authorization is expired or belongs to a different site/device', 401);
    const allowed = ['IncomingReceipt', 'StockToDisplay', 'RetailSale', 'CafeIssue', 'KitchenReturn', 'DisplayReturnToStock'];
    if (!allowed.includes(kind)) error('ADMIN_DECISION_REQUIRED', 'Unsupported or unauthorized inventory operation', 403);
    if (id(payload.transaction_id) !== event.id) error('INVALID_INVENTORY', 'Transaction id must match the owning event');
    const user = await tx.user.findFirst({ where: { id: userId, active: true, status: 'ACTIVE' },
      include: { siteRoles: { where: { OR: [{ siteId }, { siteId: null }], role: { active: true } }, include: { role: true } } } });
    if (!user?.siteRoles.some((assignment) => assignment.role.permissions.some((permission) => ['*', 'inventory.write'].includes(permission))))
      error('FORBIDDEN', 'The operator has no inventory permission for this branch', 403);
    const rows = lines(payload.lines).map((row) => {
      if (row.location !== 'FREEZER' && row.location !== 'DISPLAY') error('INVALID_INVENTORY', 'Branch Type 2 location must be stock or display');
      if (typeof row.delta_scaled !== 'string' || !/^-?[1-9]\d*$/.test(row.delta_scaled)) error('INVALID_INVENTORY', 'Inventory delta must be a nonzero integer');
      const delta = BigInt(row.delta_scaled);
      if (delta < -9_223_372_036_854_775_807n || delta > 9_223_372_036_854_775_807n) error('INVALID_INVENTORY', 'Inventory delta is out of range');
      return { itemId: id(row.item_id), location: row.location as StockLocation, deltaScaled: delta };
    });
    if (new Set(rows.map((row) => `${row.itemId}:${row.location}`)).size !== rows.length) error('INVALID_INVENTORY', 'Inventory operation repeats a location');
    const items = new Set(rows.map((row) => row.itemId));
    for (const itemId of items) {
      const legs = rows.filter((row) => row.itemId === itemId);
      if (kind === 'StockToDisplay' || kind === 'DisplayReturnToStock') {
        const source = kind === 'StockToDisplay' ? 'FREEZER' : 'DISPLAY';
        const sourceLeg = legs.find((leg) => leg.location === source);
        if (legs.length !== 2 || legs.reduce((total, leg) => total + leg.deltaScaled, 0n) !== 0n || !sourceLeg || sourceLeg.deltaScaled >= 0n)
          error('INVALID_INVENTORY', 'Internal transfer must conserve stock with one debit and one credit');
      } else {
        const target = kind === 'RetailSale' ? 'DISPLAY' : 'FREEZER';
        if (legs.length !== 1 || legs[0].location !== target || (kind === 'IncomingReceipt' ? legs[0].deltaScaled < 0n : legs[0].deltaScaled > 0n))
          error('INVALID_INVENTORY', 'Inventory location or direction does not match the business operation');
      }
    }
    if (kind === 'IncomingReceipt') {
      const receipt = await tx.kitchenIncomingReceipt.findUnique({ where: { id: referenceId }, include: { shipment: { include: { lines: true } }, lines: true } });
      if (!receipt || receipt.shipment.destinationSiteId !== siteId || !['ACCEPTED', 'RESOLVED'].includes(receipt.status))
        error('RECEIPT_NOT_ACCEPTED', 'Only an accepted or Admin-resolved receipt can add stock', 409);
      const confirmed = new Map(receipt.lines.map((row) => [receipt.shipment.lines.find((line) => line.id === row.shipmentLineId)!.itemId, row.confirmedScaled]));
      if (confirmed.size !== rows.length || rows.some((row) => confirmed.get(row.itemId) !== row.deltaScaled))
        error('INVALID_INVENTORY', 'Inventory receipt differs from the authoritative confirmed quantities');
    }
    if (['RetailSale', 'CafeIssue', 'KitchenReturn'].includes(kind)) {
      const sourceType = kind === 'RetailSale' ? 'sale.completed' : kind === 'CafeIssue' ? 'custom_order.status_changed' : 'kitchen_return.dispatched';
      const source = await tx.syncEvent.findUnique({ where: { deviceId_streamEpoch_deviceSequence: {
        deviceId: origin.deviceId, streamEpoch: origin.streamEpoch, deviceSequence: origin.deviceSequence - 1,
      } } });
      if (!source || source.siteId !== siteId || source.eventType !== sourceType)
        error('SOURCE_DOCUMENT_REQUIRED', 'Inventory deduction must immediately follow its owning business document', 409);
      const document = object(source.payload);
      const sourceReference = kind === 'RetailSale' ? document.sale_id : kind === 'CafeIssue' ? document.custom_order_id : document.return_id;
      if (id(sourceReference) !== referenceId || source.occurredAt.getTime() !== new Date(event.occurred_at).getTime())
        error('INVALID_INVENTORY', 'Inventory deduction does not reference its owning business document');
      if (kind === 'CafeIssue' && document.status !== 'DELIVERED') error('INVALID_INVENTORY', 'Only a delivered cafe order deducts inventory');
      const expected = new Map(lines(kind === 'CafeIssue' ? document.stock_lines : document.lines).map((line) => [id(line.item_id),
        quantity(kind === 'KitchenReturn' ? line.sent_scaled : line.quantity_scaled)]));
      if (expected.size !== rows.length || rows.some((row) => expected.get(row.itemId) !== -row.deltaScaled))
        error('INVALID_INVENTORY', 'Inventory deduction differs from the business document quantities');
      if (kind === 'CafeIssue') {
        const creation = await tx.syncEvent.findFirst({ where: { siteId, eventType: 'custom_order.created', payload: { path: ['custom_order_id'], equals: referenceId } } });
        if (!creation) error('SOURCE_DOCUMENT_REQUIRED', 'Cafe order has not reached the server', 409);
        const ordered = new Map(lines(object(creation.payload).lines).map((line) => [id(line.item_id), quantity(line.quantity_scaled)]));
        if (ordered.size !== expected.size || [...expected].some(([itemId, count]) => ordered.get(itemId) !== count))
          error('INVALID_INVENTORY', 'Cafe delivery quantities differ from the original immutable order');
      }
    }
    if (kind === 'DisplayReturnToStock') {
      const display = await tx.stockBalance.findMany({ where: { siteId, location: 'DISPLAY', quantityScaled: { gt: 0n } } });
      if (display.length !== rows.filter((row) => row.location === 'DISPLAY').length || display.some((balance) =>
        !rows.some((row) => row.itemId === balance.itemId && row.location === 'DISPLAY' && row.deltaScaled === -balance.quantityScaled)))
        error('DISPLAY_COUNT_MISMATCH', 'End Day must include every remaining display item', 409);
    }
    for (const row of rows) {
      const key = { siteId, itemId: row.itemId, location: row.location };
      const current = await tx.stockBalance.findUnique({ where: { siteId_itemId_location: key } });
      const next = (current?.quantityScaled ?? 0n) + row.deltaScaled;
      if (next < 0n) error('INSUFFICIENT_STOCK', 'The operation would make stock or display negative', 409);
      if (kind === 'DisplayReturnToStock' && row.location === 'DISPLAY' && next !== 0n)
        error('DISPLAY_COUNT_MISMATCH', 'End Day must return the complete counted display balance', 409);
      await tx.stockBalance.upsert({ where: { siteId_itemId_location: key },
        create: { ...key, quantityScaled: next, asOfAt: new Date(event.occurred_at), sourceEventId: event.id },
        update: { quantityScaled: next, version: { increment: 1 }, asOfAt: new Date(event.occurred_at), sourceEventId: event.id } });
    }
    await tx.inventoryTransaction.create({ data: { id: event.id, siteId, userId, referenceId, kind, reason: text(payload.reason, 500),
      occurredAt: new Date(event.occurred_at), sourceEventId: event.id, lines: { create: rows } } });
  }
}

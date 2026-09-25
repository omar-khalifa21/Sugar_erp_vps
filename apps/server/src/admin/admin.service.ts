import { HttpException, Injectable, NotFoundException } from '@nestjs/common';
import { PrismaService } from '../prisma/prisma.service';
import { CreateStockAdjustmentDto } from './create-stock-adjustment.dto';
import { DecideConflictDto } from './decide-conflict.dto';
import { CreateCafeCustomerDto } from './create-cafe-customer.dto';
import { SetCafePriceDto } from './set-cafe-price.dto';
import { UpdateCafeCustomerDto } from './update-cafe-customer.dto';
import { SaveRecipeDto } from './save-recipe.dto';

@Injectable()
export class AdminService {
  constructor(private readonly prisma: PrismaService) {}

  async recipes() { const rows = await this.prisma.recipe.findMany({ include: { product: true, components: { include: { ingredient: true } } }, orderBy: { product: { nameAr: 'asc' } } }); return rows.map((row) => ({ ...row, outputScaled: row.outputScaled.toString(), components: row.components.map((component) => ({ ...component, quantityScaled: component.quantityScaled.toString() })) })); }

  async saveRecipe(productId: string, input: SaveRecipeDto) {
    if (new Set(input.components.map((x) => x.ingredientItemId)).size !== input.components.length)
      throw new HttpException({ code: 'DUPLICATE_INGREDIENT', message: 'Recipe repeats an ingredient' }, 422);
    await this.prisma.$transaction(async (tx) => {
      const product = await tx.item.findFirst({ where: { id: productId, kind: 'PRODUCT', active: true } });
      const ingredients = await tx.item.findMany({ where: { id: { in: input.components.map((x) => x.ingredientItemId) }, kind: 'INGREDIENT', active: true } });
      if (!product || ingredients.length !== input.components.length) throw new HttpException({ code: 'INVALID_RECIPE_ITEMS', message: 'Recipe requires one active product and active ingredients' }, 422);
      const existing = await tx.recipe.findUnique({ where: { productItemId: productId } });
      if ((existing?.version ?? 0) !== input.expectedVersion) throw new HttpException({ code: 'STALE_VERSION', message: 'Recipe was updated; reload and retry', retryable: false }, 409);
      if (existing) {
        await tx.recipeComponent.deleteMany({ where: { recipeId: existing.id } });
        await tx.recipe.update({ where: { id: existing.id }, data: { outputScaled: BigInt(input.outputScaled), version: { increment: 1 }, active: true,
          components: { create: input.components.map((x) => ({ ingredientItemId: x.ingredientItemId, quantityScaled: BigInt(x.quantityScaled) })) } }, include: { components: true } });
        return;
      }
      await tx.recipe.create({ data: { productItemId: productId, outputScaled: BigInt(input.outputScaled), components: { create: input.components.map((x) => ({ ingredientItemId: x.ingredientItemId, quantityScaled: BigInt(x.quantityScaled) })) } } });
    });
    const saved = await this.prisma.recipe.findUniqueOrThrow({ where: { productItemId: productId }, include: { product: true, components: { include: { ingredient: true } } } });
    return { ...saved, outputScaled: saved.outputScaled.toString(), components: saved.components.map((component) => ({ ...component, quantityScaled: component.quantityScaled.toString() })) };
  }

  async siteOverview(siteId: string) {
    const site = await this.prisma.site.findUnique({ where: { id: siteId } });
    if (!site) throw new NotFoundException('Site not found');
    const { dayStart, nextDay, monthStart, nextMonth } = cairoBoundaries();
    const [stock, today, month, todayCorrections, monthCorrections, recentSales, pendingAdjustments, inventoryHistory] = await Promise.all([
      this.prisma.stockBalance.findMany({
        where: { siteId },
        include: { item: true },
        orderBy: [{ item: { kind: 'asc' } }, { item: { nameAr: 'asc' } }, { location: 'asc' }],
      }),
      this.prisma.retailSale.aggregate({
        where: { siteId, businessDate: { gte: dayStart, lt: nextDay } },
        _sum: { netMinor: true, tipMinor: true },
        _count: { _all: true },
      }),
      this.prisma.retailSale.aggregate({
        where: { siteId, businessDate: { gte: monthStart, lt: nextMonth } },
        _sum: { netMinor: true, tipMinor: true },
        _count: { _all: true },
      }),
      this.prisma.saleCorrection.aggregate({
        where: { originalSale: { siteId }, occurredAt: { gte: dayStart, lt: nextDay } }, _sum: { refundMinor: true },
      }),
      this.prisma.saleCorrection.aggregate({
        where: { originalSale: { siteId }, occurredAt: { gte: monthStart, lt: nextMonth } }, _sum: { refundMinor: true },
      }),
      this.prisma.retailSale.findMany({
        where: { siteId, businessDate: { gte: dayStart, lt: nextDay } },
        include: { corrections: { select: { refundMinor: true } } },
        orderBy: { occurredAt: 'desc' },
        take: 10,
      }),
      this.prisma.stockAdjustment.count({ where: { siteId, status: 'PENDING_SITE_APPLY' } }),
      this.prisma.inventoryTransaction.findMany({ where: { siteId }, include: { lines: { include: { item: true } }, user: { select: { displayName: true } } },
        orderBy: [{ occurredAt: 'desc' }, { id: 'desc' }], take: 100 }),
    ]);

    const newest = stock.reduce<Date | null>(
      (latest, row) => (!latest || row.asOfAt > latest ? row.asOfAt : latest),
      null,
    );
    return {
      site,
      inventory_history: inventoryHistory.map((movement) => ({
        id: movement.id, reference_id: movement.referenceId, kind: movement.kind, reason: movement.reason,
        user_name: movement.user.displayName, occurred_at: movement.occurredAt.toISOString(),
        lines: movement.lines.map((line) => {
          const item = line.item;
          return { item_id: line.itemId, name_ar: item?.nameAr ?? line.itemId, unit: item?.unit ?? '',
            quantity_scale: item?.quantityScale ?? 1, location: line.location, delta_scaled: line.deltaScaled.toString() };
        }),
      })),
      freshness: { as_of: newest?.toISOString() ?? null, stale: newest ? Date.now() - newest.getTime() > 15 * 60_000 : true },
      stock: stock.map((row) => ({
        id: row.id,
        item_id: row.itemId,
        sku: row.item.sku,
        name_ar: row.item.nameAr,
        kind: row.item.kind,
        unit: row.item.unit,
        retail_price_minor: row.item.retailPriceMinor,
        quantity_scale: row.item.quantityScale,
        location: row.location,
        quantity_scaled: row.quantityScaled.toString(),
        version: row.version,
        as_of: row.asOfAt.toISOString(),
      })),
      sales: {
        currency: 'EGP',
        today: {
          net_minor: ((today._sum.netMinor ?? 0n) - (todayCorrections._sum.refundMinor ?? 0n)).toString(),
          tips_minor: (today._sum.tipMinor ?? 0n).toString(),
          receipt_count: today._count._all,
        },
        month: {
          net_minor: ((month._sum.netMinor ?? 0n) - (monthCorrections._sum.refundMinor ?? 0n)).toString(),
          tips_minor: (month._sum.tipMinor ?? 0n).toString(),
          receipt_count: month._count._all,
        },
        recent: recentSales.map((sale) => ({
          id: sale.id,
          receipt_number: sale.receiptNumber,
          shift_kind: sale.shiftKind,
          net_minor: (sale.netMinor - sale.corrections.reduce((sum, correction) => sum + correction.refundMinor, 0n)).toString(),
          tip_minor: sale.tipMinor.toString(),
          occurred_at: sale.occurredAt.toISOString(),
        })),
      },
      pending_adjustments: pendingAdjustments,
    };
  }

  async requestStockAdjustment(siteId: string, userId: string, input: CreateStockAdjustmentDto) {
    const balance = await this.prisma.stockBalance.findUnique({
      where: { siteId_itemId_location: { siteId, itemId: input.itemId, location: input.location } },
    });
    if (!balance) throw new NotFoundException('Stock balance not found');
    if (balance.version !== input.expectedVersion) {
      throw new HttpException(
        {
          code: 'STALE_VERSION',
          message: 'Stock changed after this screen was loaded',
          retryable: false,
          current_version: balance.version,
        },
        409,
      );
    }
    const adjustment = await this.prisma.stockAdjustment.create({
      data: {
        siteId,
        itemId: input.itemId,
        location: input.location,
        deltaScaled: BigInt(input.deltaScaled),
        reason: input.reason.trim(),
        expectedVersion: input.expectedVersion,
        createdByUserId: userId,
      },
    });
    return {
      id: adjustment.id,
      status: adjustment.status,
      delta_scaled: adjustment.deltaScaled.toString(),
      created_at: adjustment.createdAt.toISOString(),
    };
  }

  listAdjustments(siteId: string) {
    return this.prisma.stockAdjustment.findMany({
      where: { siteId },
      include: { item: { select: { sku: true, nameAr: true, unit: true } }, createdBy: { select: { displayName: true } } },
      orderBy: { createdAt: 'desc' },
      take: 100,
    }).then((rows) => rows.map((row) => ({
      id: row.id,
      item: row.item,
      location: row.location,
      delta_scaled: row.deltaScaled.toString(),
      reason: row.reason,
      status: row.status,
      created_by: row.createdBy.displayName,
      created_at: row.createdAt.toISOString(),
      applied_at: row.appliedAt?.toISOString() ?? null,
    })));
  }

  async cafeOverview() {
    const customers = await this.prisma.cafeCustomer.findMany({
      where: { active: true },
      include: {
        invoices: {
          include: {
            issuingSite: { select: { id: true, name: true } },
            allocations: { include: { payment: { select: { reversedAt: true } } } },
            lines: { orderBy: { itemNameSnapshot: 'asc' } },
          },
          orderBy: { occurredAt: 'desc' },
        },
        payments: {
          where: { reversedAt: null },
          include: { collectedAtSite: { select: { id: true, name: true } } },
          orderBy: { occurredAt: 'desc' },
        },
        prices: {
          include: { item: { select: { id: true, sku: true, nameAr: true, unit: true, retailPriceMinor: true } } },
          orderBy: { item: { nameAr: 'asc' } },
        },
      },
      orderBy: { name: 'asc' },
    });
    let invoiced = 0n;
    let collected = 0n;
    const result = customers.map((customer) => {
      const customerInvoiced = customer.invoices
        .filter((invoice) => invoice.status !== 'REVERSED')
        .reduce((sum, invoice) => sum + invoice.netMinor, 0n);
      const customerCollected = customer.payments.reduce((sum, payment) => sum + payment.amountMinor, 0n);
      invoiced += customerInvoiced;
      collected += customerCollected;
      return {
        id: customer.id,
        code: customer.code,
        name: customer.name,
        contact: customer.contact,
        notes: customer.notes,
        prices: customer.prices.map((price) => ({
          id: price.id,
          item_id: price.itemId,
          sku: price.item.sku,
          name_ar: price.item.nameAr,
          unit: price.item.unit,
          retail_price_minor: price.item.retailPriceMinor,
          price_minor: price.priceMinor,
          version: price.version,
          updated_at: price.updatedAt.toISOString(),
        })),
        invoiced_minor: customerInvoiced.toString(),
        collected_minor: customerCollected.toString(),
        outstanding_minor: (customerInvoiced - customerCollected).toString(),
        invoices: customer.invoices.map((invoice) => {
          const allocated = invoice.allocations
            .filter((allocation) => !allocation.payment.reversedAt)
            .reduce((sum, allocation) => sum + allocation.amountMinor, 0n);
          return {
            id: invoice.id,
            number: invoice.invoiceNumber,
            issuing_site: invoice.issuingSite,
            business_date: invoice.businessDate.toISOString().slice(0, 10),
            net_minor: invoice.netMinor.toString(),
            paid_minor: allocated.toString(),
            outstanding_minor: (invoice.status === 'REVERSED' ? 0n : invoice.netMinor - allocated).toString(),
            status: invoice.status,
            lines: invoice.lines.map((line) => ({
              id: line.id,
              item_id: line.itemId,
              name_ar: line.itemNameSnapshot,
              sku: line.skuSnapshot,
              unit: line.unitSnapshot,
              quantity_scaled: line.quantityScaled.toString(),
              quantity_scale: line.quantityScale,
              unit_price_minor: line.unitPriceMinor,
              total_minor: line.totalMinor.toString(),
            })),
          };
        }),
        payments: customer.payments.map((payment) => ({
          id: payment.id,
          reference: payment.reference,
          amount_minor: payment.amountMinor.toString(),
          collected_at_site: payment.collectedAtSite,
          occurred_at: payment.occurredAt.toISOString(),
        })),
      };
    });
    return {
      currency: 'EGP',
      totals: {
        invoiced_minor: invoiced.toString(),
        collected_minor: collected.toString(),
        outstanding_minor: (invoiced - collected).toString(),
      },
      customers: result,
    };
  }

  createCafeCustomer(input: CreateCafeCustomerDto) {
    return this.prisma.cafeCustomer.create({
      data: {
        code: input.code.trim().toUpperCase(),
        name: input.name.trim(),
        contact: input.contact?.trim() || null,
        notes: input.notes?.trim() || null,
      },
    });
  }

  updateCafeCustomer(id: string, input: UpdateCafeCustomerDto) {
    return this.prisma.cafeCustomer.update({
      where: { id },
      data: {
        ...(input.code !== undefined ? { code: input.code.trim().toUpperCase() } : {}),
        ...(input.name !== undefined ? { name: input.name.trim() } : {}),
        ...(input.contact !== undefined ? { contact: input.contact.trim() || null } : {}),
        ...(input.notes !== undefined ? { notes: input.notes.trim() || null } : {}),
        ...(input.active !== undefined ? { active: input.active } : {}),
        version: { increment: 1 },
      },
    });
  }

  archiveCafeCustomer(id: string) {
    return this.prisma.cafeCustomer.update({ where: { id }, data: { active: false, version: { increment: 1 } } });
  }

  setCafePrice(customerId: string, itemId: string, input: SetCafePriceDto) {
    return this.prisma.$transaction(async (transaction) => {
      await transaction.cafeCustomer.findUniqueOrThrow({ where: { id: customerId } });
      const item = await transaction.item.findFirst({ where: { id: itemId, kind: 'PRODUCT', active: true } });
      if (!item) throw new HttpException({ code: 'INVALID_CAFE_PRODUCT', message: 'Cafe prices require an active product' }, 422);
      const current = await transaction.cafeItemPrice.findUnique({
        where: { customerId_itemId: { customerId, itemId } },
      });
      if (current?.priceMinor === input.priceMinor) return current;
      const price = current
        ? await transaction.cafeItemPrice.update({
            where: { id: current.id },
            data: { priceMinor: input.priceMinor, version: { increment: 1 } },
          })
        : await transaction.cafeItemPrice.create({
            data: { customerId, itemId, priceMinor: input.priceMinor },
          });
      await transaction.cafePriceRevision.create({
        data: {
          cafePriceId: price.id,
          customerId,
          itemId,
          priceMinor: price.priceMinor,
          version: price.version,
        },
      });
      return price;
    });
  }

  async kitchenOverview() {
    const sites = await this.prisma.site.findMany({ where: { type: 'KITCHEN', active: true } });
    const siteIds = sites.map((site) => site.id);
    const [stock, variances, requests, shipments] = await Promise.all([
      this.prisma.stockBalance.findMany({
        where: { siteId: { in: siteIds }, item: { kind: 'INGREDIENT' } },
        include: { site: { select: { id: true, name: true } }, item: true },
        orderBy: { item: { nameAr: 'asc' } },
      }),
      this.prisma.ingredientVariance.findMany({
        where: { siteId: { in: siteIds } },
        include: { site: { select: { id: true, name: true } }, item: true },
        orderBy: { businessDate: 'desc' },
        take: 100,
      }),
      this.prisma.kitchenRequest.findMany({
        include: { requestingSite: { select: { id: true, name: true } }, lines: true },
        orderBy: { submittedAt: 'desc' }, take: 100,
      }),
      this.prisma.kitchenShipment.findMany({
        include: { destinationSite: { select: { id: true, name: true } }, lines: true, receipt: true },
        orderBy: { dispatchedAt: 'desc' }, take: 100,
      }),
    ]);
    return {
      sites,
      stock: stock.map((row) => ({
        id: row.id,
        site: row.site,
        item_id: row.itemId,
        name_ar: row.item.nameAr,
        sku: row.item.sku,
        unit: row.item.unit,
        quantity_scale: row.item.quantityScale,
        quantity_scaled: row.quantityScaled.toString(),
        as_of: row.asOfAt.toISOString(),
      })),
      variances: variances.map((row) => ({
        id: row.id,
        site: row.site,
        item_id: row.itemId,
        name_ar: row.item.nameAr,
        unit: row.item.unit,
        quantity_scale: row.item.quantityScale,
        business_date: row.businessDate.toISOString().slice(0, 10),
        expected_scaled: row.expectedScaled.toString(),
        actual_scaled: row.actualScaled.toString(),
        recorded_waste_scaled: row.recordedWasteScaled.toString(),
        unexplained_variance_scaled: row.unexplainedVarianceScaled.toString(),
        cost_minor_per_scale: row.costMinorPerScale?.toString() ?? null,
      })),
      requests: requests.map((row) => ({
        id: row.id, branch: row.requestingSite, status: row.status,
        workflow_status: row.status === 'REQUESTED' || row.status === 'RECEIVED' ? 'PENDING' : 'SENT',
        submitted_at: row.submittedAt.toISOString(), line_count: row.lines.length,
      })),
      shipments: shipments.map((row) => ({
        id: row.id, request_id: row.requestId, reference: row.reference, branch: row.destinationSite, status: row.status,
        workflow_status: row.status === 'CONFLICT' ? 'CONFLICTED' : row.status === 'RECEIVED' || row.status === 'RESOLVED' ? 'CONFIRMED' : 'SENT',
        dispatched_at: row.dispatchedAt.toISOString(), line_count: row.lines.length,
        receipt_status: row.receipt?.status ?? null,
      })),
    };
  }

  async conflicts() {
    const rows = await this.prisma.quantityConflict.findMany({
      include: {
        site: { select: { id: true, name: true } },
        lines: { include: { item: true }, orderBy: { item: { nameAr: 'asc' } } },
        decision: { include: { adminUser: { select: { displayName: true } } } },
      },
      orderBy: { reportedAt: 'desc' },
      take: 100,
    });
    return rows.map((row) => ({
      id: row.id,
      site: row.site,
      reference: row.reference,
      status: row.status,
      version: row.version,
      note: row.note,
      sent_at: row.sentAt.toISOString(),
      counted_at: row.countedAt.toISOString(),
      reported_at: row.reportedAt.toISOString(),
      lines: row.lines.map((line) => ({
        id: line.id,
        item_id: line.itemId,
        name_ar: line.item.nameAr,
        unit: line.item.unit,
        quantity_scale: line.item.quantityScale,
        sent_scaled: line.sentScaled.toString(),
        counted_scaled: line.countedScaled.toString(),
        final_scaled: line.finalScaled?.toString() ?? null,
        note: line.note,
      })),
      decision: row.decision
        ? {
            reason: row.decision.reason,
            application_status: row.decision.applicationStatus,
            decided_at: row.decision.decidedAt.toISOString(),
            admin_name: row.decision.adminUser.displayName,
          }
        : null,
    }));
  }

  async decideConflict(id: string, userId: string, input: DecideConflictDto) {
    return this.prisma.$transaction(async (transaction) => {
      const conflict = await transaction.quantityConflict.findUnique({
        where: { id },
        include: { lines: true, decision: true },
      });
      if (!conflict) throw new NotFoundException('Conflict not found');
      if (conflict.status !== 'OPEN' || conflict.decision) {
        throw new HttpException({ code: 'CONFLICT_ALREADY_DECIDED', message: 'Conflict already has a decision', retryable: false }, 409);
      }
      if (conflict.version !== input.expectedVersion) {
        throw new HttpException({ code: 'STALE_VERSION', message: 'Conflict changed after this screen was loaded', retryable: false, current_version: conflict.version }, 409);
      }
      const expectedLineIds = new Set(conflict.lines.map((line) => line.id));
      const suppliedLineIds = new Set(input.lines.map((line) => line.lineId));
      if (expectedLineIds.size !== suppliedLineIds.size || [...expectedLineIds].some((lineId) => !suppliedLineIds.has(lineId))) {
        throw new HttpException({ code: 'VALIDATION_ERROR', message: 'A final quantity is required for every conflict line', retryable: false }, 422);
      }
      const changed = await transaction.quantityConflict.updateMany({
        where: { id, status: 'OPEN', version: input.expectedVersion },
        data: { status: 'PENDING_SITE_APPLY', version: { increment: 1 } },
      });
      if (changed.count !== 1) {
        throw new HttpException({ code: 'STALE_VERSION', message: 'Conflict changed while the decision was saved', retryable: false }, 409);
      }
      for (const line of input.lines) {
        await transaction.conflictLine.update({ where: { id: line.lineId }, data: { finalScaled: BigInt(line.finalScaled) } });
      }
      const decision = await transaction.adminDecision.create({
        data: { conflictId: id, adminUserId: userId, expectedVersion: input.expectedVersion, reason: input.reason.trim() },
      });
      return { id: decision.id, conflict_id: id, application_status: decision.applicationStatus, decided_at: decision.decidedAt.toISOString() };
    });
  }
}

function cairoBoundaries() {
  const nowParts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Africa/Cairo',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date());
  const [year, month, day] = nowParts.split('-').map(Number);
  return {
    dayStart: new Date(Date.UTC(year, month - 1, day)),
    nextDay: new Date(Date.UTC(year, month - 1, day + 1)),
    monthStart: new Date(Date.UTC(year, month - 1, 1)),
    nextMonth: new Date(Date.UTC(year, month, 1)),
  };
}

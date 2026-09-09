import {
  CafeInvoiceStatus,
  DeviceProfile,
  EnrollmentStatus,
  ItemKind,
  PrismaClient,
  ShiftKind,
  SiteType,
  StockLocation,
} from '@prisma/client';
import * as argon2 from 'argon2';

const prisma = new PrismaClient();

async function main(): Promise<void> {
  const username = process.env.DEV_ADMIN_USERNAME?.trim().toLowerCase() || 'admin';
  const password = process.env.DEV_ADMIN_PASSWORD;
  if (!password || password.length < 12) {
    throw new Error('DEV_ADMIN_PASSWORD must contain at least 12 characters');
  }

  const role = await prisma.role.upsert({
    where: { code: 'ADMIN' },
    update: { active: true },
    create: {
      code: 'ADMIN',
      name: 'Administrator',
      permissions: ['*'],
    },
  });

  const passwordHash = await argon2.hash(password);
  const user = await prisma.user.upsert({
    where: { username },
    update: {
      displayName: 'Development Administrator',
      passwordHash,
      active: true,
    },
    create: {
      username,
      displayName: 'Development Administrator',
      passwordHash,
    },
  });

  const assignment = await prisma.userSiteRole.findFirst({
    where: { userId: user.id, roleId: role.id, siteId: null },
  });
  if (!assignment) {
    await prisma.userSiteRole.create({ data: { userId: user.id, roleId: role.id } });
  }

  if (process.env.DEV_SEED_SYNTHETIC !== 'true') return;

  const roles = [
    { code: 'MANAGER', name: 'Branch manager', permissions: ['sales.read', 'stock.read', 'reports.read'] },
    { code: 'CASHIER', name: 'Cashier', permissions: ['sales.create', 'sales.current_shift.read'] },
    { code: 'KITCHEN', name: 'Kitchen operator', permissions: ['kitchen.dispatch', 'ingredients.read'] },
  ];
  for (const demoRole of roles) {
    await prisma.role.upsert({
      where: { code: demoRole.code },
      update: { name: demoRole.name, permissions: demoRole.permissions, active: true },
      create: demoRole,
    });
  }

  const siteInputs = [
    { code: 'BRANCH-ZAMALEK', name: 'فرع الزمالك', type: SiteType.BRANCH_TYPE_1 },
    { code: 'BRANCH-NASR', name: 'فرع مدينة نصر', type: SiteType.BRANCH_TYPE_2 },
    { code: 'KITCHEN-CENTRAL', name: 'المطبخ المركزي', type: SiteType.KITCHEN },
  ];
  const sites = [];
  for (const input of siteInputs) {
    sites.push(
      await prisma.site.upsert({
        where: { code: input.code },
        update: { name: input.name, type: input.type, active: true },
        create: input,
      }),
    );
  }

  const itemInputs = [
    { sku: 'CAKE-CHOC', nameAr: 'تورتة شوكولاتة', unit: 'قطعة', quantityScale: 1, retailPriceMinor: 45000, kind: ItemKind.PRODUCT },
    { sku: 'GATEAUX-001', nameAr: 'جاتوه شوكولاتة', unit: 'قطعة', quantityScale: 1, retailPriceMinor: 8500, kind: ItemKind.PRODUCT },
    { sku: 'ING-EGGS', nameAr: 'بيض', unit: 'بيضة', quantityScale: 1, retailPriceMinor: 700, kind: ItemKind.INGREDIENT },
    { sku: 'ING-FLOUR', nameAr: 'دقيق', unit: 'كجم', quantityScale: 1000, retailPriceMinor: 4200, kind: ItemKind.INGREDIENT },
    { sku: 'ING-SUGAR', nameAr: 'سكر', unit: 'كجم', quantityScale: 1000, retailPriceMinor: 3800, kind: ItemKind.INGREDIENT },
  ];
  const items = [];
  for (const input of itemInputs) {
    items.push(
      await prisma.item.upsert({
        where: { sku: input.sku },
        update: { ...input, active: true },
        create: input,
      }),
    );
  }
  for (const item of items) {
    await prisma.retailPriceRevision.upsert({
      where: { itemId_version: { itemId: item.id, version: item.version } },
      update: { priceMinor: item.retailPriceMinor },
      create: { itemId: item.id, priceMinor: item.retailPriceMinor, version: item.version },
    });
  }

  const profileForType: Record<SiteType, DeviceProfile> = {
    BRANCH_TYPE_1: DeviceProfile.BRANCH_TYPE_1,
    BRANCH_TYPE_2: DeviceProfile.BRANCH_TYPE_2,
    KITCHEN: DeviceProfile.KITCHEN,
  };
  for (const site of sites) {
    const keyThumbprint = `development-only-${site.code.toLowerCase()}`;
    const device = await prisma.device.upsert({
      where: { keyThumbprint },
      update: {
        siteId: site.id,
        profile: profileForType[site.type],
        enrollmentStatus: EnrollmentStatus.ENROLLED,
        activeWriter: true,
        lastSeenAt: new Date(),
        appVersion: '0.1.0-demo',
      },
      create: {
        siteId: site.id,
        profile: profileForType[site.type],
        enrollmentStatus: EnrollmentStatus.ENROLLED,
        activeWriter: true,
        keyThumbprint,
        lastSeenAt: new Date(),
        appVersion: '0.1.0-demo',
      },
    });
    await prisma.deviceSyncState.upsert({
      where: { deviceId: device.id },
      update: {},
      create: { deviceId: device.id },
    });
    await prisma.deviceSyncCursor.upsert({
      where: { deviceId: device.id },
      update: {},
      create: { deviceId: device.id },
    });
  }

  const now = new Date();
  const balances = [
    { site: 0, item: 0, location: StockLocation.SALEABLE, quantity: 18n },
    { site: 0, item: 1, location: StockLocation.SALEABLE, quantity: 46n },
    { site: 1, item: 0, location: StockLocation.FREEZER, quantity: 72n },
    { site: 1, item: 0, location: StockLocation.DISPLAY, quantity: 14n },
    { site: 1, item: 1, location: StockLocation.FREEZER, quantity: 55n },
    { site: 1, item: 1, location: StockLocation.DISPLAY, quantity: 22n },
    { site: 2, item: 2, location: StockLocation.KITCHEN, quantity: 700n },
    { site: 2, item: 3, location: StockLocation.KITCHEN, quantity: 12500n },
    { site: 2, item: 4, location: StockLocation.KITCHEN, quantity: 9300n },
  ];
  for (const balance of balances) {
    await prisma.stockBalance.upsert({
      where: {
        siteId_itemId_location: {
          siteId: sites[balance.site].id,
          itemId: items[balance.item].id,
          location: balance.location,
        },
      },
      update: { quantityScaled: balance.quantity, asOfAt: now },
      create: {
        siteId: sites[balance.site].id,
        itemId: items[balance.item].id,
        location: balance.location,
        quantityScaled: balance.quantity,
        asOfAt: now,
      },
    });
  }

  const cairoDate = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Africa/Cairo',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(now);
  const businessDate = new Date(`${cairoDate}T00:00:00.000Z`);
  const [year, month, day] = cairoDate.split('-').map(Number);
  const earlierDate = new Date(Date.UTC(year, month - 1, Math.max(1, day - 3)));
  const sales = [
    { site: 0, receipt: `${cairoDate}-Z-001`, date: businessDate, shift: ShiftKind.MORNING, net: 795000n, tip: 25000n },
    { site: 0, receipt: `${cairoDate}-Z-002`, date: businessDate, shift: ShiftKind.EVENING, net: 450000n, tip: 10000n },
    { site: 0, receipt: `${cairoDate}-Z-MONTH`, date: earlierDate, shift: ShiftKind.MORNING, net: 2015000n, tip: 40000n },
    { site: 1, receipt: `${cairoDate}-N-001`, date: businessDate, shift: ShiftKind.MORNING, net: 310000n, tip: 5000n },
    { site: 1, receipt: `${cairoDate}-N-002`, date: businessDate, shift: ShiftKind.EVENING, net: 230000n, tip: 0n },
    { site: 1, receipt: `${cairoDate}-N-MONTH`, date: earlierDate, shift: ShiftKind.MORNING, net: 960000n, tip: 15000n },
  ];
  for (const sale of sales) {
    await prisma.retailSale.upsert({
      where: { siteId_receiptNumber: { siteId: sites[sale.site].id, receiptNumber: sale.receipt } },
      update: { businessDate: sale.date, shiftKind: sale.shift, netMinor: sale.net, tipMinor: sale.tip },
      create: {
        siteId: sites[sale.site].id,
        receiptNumber: sale.receipt,
        businessDate: sale.date,
        shiftKind: sale.shift,
        netMinor: sale.net,
        tipMinor: sale.tip,
        occurredAt: now,
      },
    });
  }

  const customer = await prisma.cafeCustomer.upsert({
    where: { code: 'CAFE-NILE' },
    update: { name: 'كافيه النيل', contact: '0100 555 0101', notes: 'تسليم يومي صباحاً', active: true },
    create: { code: 'CAFE-NILE', name: 'كافيه النيل', contact: '0100 555 0101', notes: 'تسليم يومي صباحاً' },
  });
  const secondCustomer = await prisma.cafeCustomer.upsert({
    where: { code: 'CAFE-GARDEN' },
    update: { name: 'جاردن كافيه', contact: 'accounts@garden.demo', notes: 'تحصيل أسبوعي', active: true },
    create: { code: 'CAFE-GARDEN', name: 'جاردن كافيه', contact: 'accounts@garden.demo', notes: 'تحصيل أسبوعي' },
  });
  const cafePriceInputs = [
    { customer: customer.id, item: items[0].id, price: 42000 },
    { customer: customer.id, item: items[1].id, price: 7600 },
    { customer: secondCustomer.id, item: items[0].id, price: 40000 },
    { customer: secondCustomer.id, item: items[1].id, price: 5000 },
  ];
  for (const cafePriceInput of cafePriceInputs) {
    const cafePrice = await prisma.cafeItemPrice.upsert({
      where: { customerId_itemId: { customerId: cafePriceInput.customer, itemId: cafePriceInput.item } },
      update: { priceMinor: cafePriceInput.price },
      create: { customerId: cafePriceInput.customer, itemId: cafePriceInput.item, priceMinor: cafePriceInput.price },
    });
    await prisma.cafePriceRevision.upsert({
      where: { cafePriceId_version: { cafePriceId: cafePrice.id, version: cafePrice.version } },
      update: { priceMinor: cafePrice.priceMinor },
      create: { cafePriceId: cafePrice.id, customerId: cafePrice.customerId, itemId: cafePrice.itemId, priceMinor: cafePrice.priceMinor, version: cafePrice.version },
    });
  }
  const invoice = await prisma.cafeInvoice.upsert({
    where: { issuingSiteId_invoiceNumber: { issuingSiteId: sites[1].id, invoiceNumber: `${cairoDate}-CAFE-001` } },
    update: { customerId: customer.id, businessDate, netMinor: 8000000n, status: CafeInvoiceStatus.PARTIALLY_PAID },
    create: { customerId: customer.id, issuingSiteId: sites[1].id, invoiceNumber: `${cairoDate}-CAFE-001`, businessDate, netMinor: 8000000n, status: CafeInvoiceStatus.PARTIALLY_PAID, occurredAt: now },
  });
  const secondInvoice = await prisma.cafeInvoice.upsert({
    where: { issuingSiteId_invoiceNumber: { issuingSiteId: sites[2].id, invoiceNumber: `${cairoDate}-CAFE-002` } },
    update: { customerId: secondCustomer.id, businessDate, netMinor: 2500000n, status: CafeInvoiceStatus.OPEN },
    create: { customerId: secondCustomer.id, issuingSiteId: sites[2].id, invoiceNumber: `${cairoDate}-CAFE-002`, businessDate, netMinor: 2500000n, status: CafeInvoiceStatus.OPEN, occurredAt: now },
  });
  const payment = await prisma.cafePayment.upsert({
    where: { reference: `${cairoDate}-PAY-001` },
    update: { customerId: customer.id, collectedAtSiteId: sites[1].id, amountMinor: 5000000n, occurredAt: now, reversedAt: null },
    create: { customerId: customer.id, collectedAtSiteId: sites[1].id, reference: `${cairoDate}-PAY-001`, amountMinor: 5000000n, occurredAt: now },
  });
  await prisma.cafePaymentAllocation.upsert({
    where: { paymentId_invoiceId: { paymentId: payment.id, invoiceId: invoice.id } },
    update: { amountMinor: 5000000n },
    create: { paymentId: payment.id, invoiceId: invoice.id, amountMinor: 5000000n },
  });
  const invoiceLines = [
    { invoiceId: invoice.id, item: items[0], quantity: 100n, unitPrice: 42000, total: 4200000n },
    { invoiceId: invoice.id, item: items[1], quantity: 500n, unitPrice: 7600, total: 3800000n },
    { invoiceId: secondInvoice.id, item: items[0], quantity: 50n, unitPrice: 40000, total: 2000000n },
    { invoiceId: secondInvoice.id, item: items[1], quantity: 100n, unitPrice: 5000, total: 500000n },
  ];
  for (const line of invoiceLines) {
    await prisma.cafeInvoiceLine.upsert({
      where: { invoiceId_itemId: { invoiceId: line.invoiceId, itemId: line.item.id } },
      update: {
        quantityScaled: line.quantity,
        quantityScale: line.item.quantityScale,
        unitPriceMinor: line.unitPrice,
        totalMinor: line.total,
        itemNameSnapshot: line.item.nameAr,
        skuSnapshot: line.item.sku,
        unitSnapshot: line.item.unit,
      },
      create: {
        invoiceId: line.invoiceId,
        itemId: line.item.id,
        quantityScaled: line.quantity,
        quantityScale: line.item.quantityScale,
        unitPriceMinor: line.unitPrice,
        totalMinor: line.total,
        itemNameSnapshot: line.item.nameAr,
        skuSnapshot: line.item.sku,
        unitSnapshot: line.item.unit,
      },
    });
  }

  const conflict = await prisma.quantityConflict.upsert({
    where: { sourceEventId: '11111111-1111-4111-8111-111111111111' },
    update: { siteId: sites[0].id, reference: 'SHIP-DEMO-001', note: 'الفرع عدّ كمية أقل من المرسل' },
    create: { siteId: sites[0].id, reference: 'SHIP-DEMO-001', note: 'الفرع عدّ كمية أقل من المرسل', sentAt: new Date(now.getTime() - 90 * 60_000), countedAt: new Date(now.getTime() - 25 * 60_000), sourceEventId: '11111111-1111-4111-8111-111111111111' },
  });
  await prisma.conflictLine.upsert({
    where: { conflictId_itemId: { conflictId: conflict.id, itemId: items[0].id } },
    update: { sentScaled: 20n, countedScaled: 18n },
    create: { conflictId: conflict.id, itemId: items[0].id, sentScaled: 20n, countedScaled: 18n, note: 'عبوتان غير موجودتين عند العد' },
  });

  const variances = [
    { item: 2, expected: 700n, actual: 200n, waste: 50n, unexplained: 500n, cost: 350n },
    { item: 3, expected: 15000n, actual: 12500n, waste: 500n, unexplained: 2500n, cost: 4200n },
    { item: 4, expected: 10000n, actual: 9300n, waste: 200n, unexplained: 700n, cost: 3800n },
  ];
  for (const variance of variances) {
    await prisma.ingredientVariance.upsert({
      where: { siteId_itemId_businessDate: { siteId: sites[2].id, itemId: items[variance.item].id, businessDate } },
      update: { expectedScaled: variance.expected, actualScaled: variance.actual, recordedWasteScaled: variance.waste, unexplainedVarianceScaled: variance.unexplained, costMinorPerScale: variance.cost },
      create: { siteId: sites[2].id, itemId: items[variance.item].id, businessDate, expectedScaled: variance.expected, actualScaled: variance.actual, recordedWasteScaled: variance.waste, unexplainedVarianceScaled: variance.unexplained, costMinorPerScale: variance.cost },
    });
  }
}

main()
  .finally(async () => prisma.$disconnect())
  .catch((error: unknown) => {
    console.error(error);
    process.exitCode = 1;
  });

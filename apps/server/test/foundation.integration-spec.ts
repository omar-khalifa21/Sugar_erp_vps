import { INestApplication } from '@nestjs/common';
import { Test } from '@nestjs/testing';
import * as argon2 from 'argon2';
import { randomUUID } from 'node:crypto';
import request = require('supertest');
import { AppModule } from '../src/app.module';
import { configureApp } from '../src/configure-app';
import { PrismaService } from '../src/prisma/prisma.service';

describe('VPS foundation (PostgreSQL integration)', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let token: string;
  let userId: string;
  let siteId: string;
  let itemId: string;
  let deviceId: string;
  let managedUserId: string;
  let managedRoleId: string;
  let cafeCustomerId: string;
  let cafeInvoiceId: string;
  let cafeSiteId: string;
  const suffix = randomUUID().slice(0, 8).toUpperCase();
  const username = `foundation-${suffix.toLowerCase()}`;

  beforeAll(async () => {
    const moduleRef = await Test.createTestingModule({ imports: [AppModule] }).compile();
    app = moduleRef.createNestApplication();
    configureApp(app);
    await app.init();
    prisma = moduleRef.get(PrismaService);

    const role = await prisma.role.upsert({
      where: { code: 'ADMIN' },
      update: { active: true },
      create: { code: 'ADMIN', name: 'Administrator', permissions: ['*'] },
    });
    const user = await prisma.user.create({
      data: {
        username,
        displayName: 'Integration Admin',
        passwordHash: await argon2.hash('integration-password', {
          memoryCost: 4096,
          timeCost: 1,
        }),
      },
    });
    userId = user.id;
    await prisma.userSiteRole.create({ data: { userId: user.id, roleId: role.id } });
    const login = await request(app.getHttpServer()).post('/api/v1/auth/login').send({
      username: username.toUpperCase(),
      password: 'integration-password',
    });
    token = login.body.access_token as string;
  }, 120_000);

  afterAll(async () => {
    if (cafeInvoiceId) {
      await prisma.cafeInvoiceLine.deleteMany({ where: { invoiceId: cafeInvoiceId } });
      await prisma.cafeInvoice.delete({ where: { id: cafeInvoiceId } });
    }
    if (cafeCustomerId) {
      await prisma.cafePriceRevision.deleteMany({ where: { customerId: cafeCustomerId } });
      await prisma.cafeItemPrice.deleteMany({ where: { customerId: cafeCustomerId } });
      await prisma.cafeCustomer.delete({ where: { id: cafeCustomerId } });
    }
    if (managedUserId) {
      await prisma.userSiteRole.deleteMany({ where: { userId: managedUserId } });
      await prisma.user.delete({ where: { id: managedUserId } });
    }
    if (managedRoleId) await prisma.role.delete({ where: { id: managedRoleId } });
    if (deviceId) await prisma.device.deleteMany({ where: { id: deviceId } });
    if (itemId) {
      await prisma.retailPriceRevision.deleteMany({ where: { itemId } });
      await prisma.item.delete({ where: { id: itemId } });
    }
    if (siteId) { await prisma.syncEvent.deleteMany({ where: { siteId } }); await prisma.device.deleteMany({ where: { siteId } }); await prisma.site.delete({ where: { id: siteId } }); }
    if (cafeSiteId) { await prisma.syncEvent.deleteMany({ where: { siteId: cafeSiteId } }); await prisma.device.deleteMany({ where: { siteId: cafeSiteId } }); await prisma.site.delete({ where: { id: cafeSiteId } }); }
    if (userId) {
      await prisma.userSiteRole.deleteMany({ where: { userId } });
      await prisma.user.delete({ where: { id: userId } });
    }
    await app.close();
  });

  it('separates liveness from database readiness', async () => {
    await request(app.getHttpServer()).get('/api/v1/health').expect(200).expect({
      status: 'ok',
      service: 'sugar-erp-api',
    });
    await request(app.getHttpServer()).get('/api/v1/ready').expect(200).expect({
      status: 'ready',
      database: 'ready',
      migrations: 'ready',
    });
  });

  it('rejects anonymous access and authenticates the seeded account', async () => {
    await request(app.getHttpServer()).get('/api/v1/sites').expect(401);
    const response = await request(app.getHttpServer()).post('/api/v1/auth/login').send({
      username: username.toUpperCase(),
      password: 'integration-password',
    });
    expect(response.status).toBe(201);
    expect(response.body.access_token).toEqual(expect.any(String));
  });

  it('creates the site, item, and matching-profile device foundations', async () => {
    const siteResponse = await request(app.getHttpServer())
      .post('/api/v1/sites')
      .set('Authorization', `Bearer ${token}`)
      .send({ code: `branch-${suffix}`, name: 'فرع أ', type: 'BRANCH_TYPE_1' })
      .expect(201);
    siteId = siteResponse.body.id as string;

    const itemResponse = await request(app.getHttpServer())
      .post('/api/v1/items')
      .set('Authorization', `Bearer ${token}`)
      .send({
        sku: `cake-${suffix}`,
        nameAr: 'كيكة',
        unit: 'piece',
        quantityScale: 1,
        retailPriceMinor: 34500,
        kind: 'PRODUCT',
      })
      .expect(201);
    itemId = itemResponse.body.id as string;
    expect(itemResponse.body.retailPriceMinor).toBe(34500);

    const repriced = await request(app.getHttpServer())
      .patch(`/api/v1/items/${itemId}`)
      .set('Authorization', `Bearer ${token}`)
      .send({ retailPriceMinor: 36000 })
      .expect(200);
    expect(repriced.body.retailPriceMinor).toBe(36000);
    await expect(prisma.retailPriceRevision.count({ where: { itemId } })).resolves.toBe(2);

    const deviceResponse = await request(app.getHttpServer())
      .post('/api/v1/devices')
      .set('Authorization', `Bearer ${token}`)
      .send({ siteId: siteResponse.body.id, profile: 'BRANCH_TYPE_1' })
      .expect(201);
    deviceId = deviceResponse.body.id as string;

    const mismatch = await request(app.getHttpServer())
      .post('/api/v1/devices')
      .set('Authorization', `Bearer ${token}`)
      .send({ siteId: siteResponse.body.id, profile: 'KITCHEN' });
    expect(mismatch.status).toBe(422);
    expect(mismatch.body.code).toBe('BUSINESS_RULE_VIOLATION');
  });

  it('creates users and manages friendly role privileges', async () => {
    const role = await request(app.getHttpServer())
      .post('/api/v1/roles')
      .set('Authorization', `Bearer ${token}`)
      .send({ code: `CASHIER-${suffix}`, name: 'كاشير اختبار', permissions: ['sales.create', 'stock.read'] })
      .expect(201);
    managedRoleId = role.body.id as string;
    const user = await request(app.getHttpServer())
      .post('/api/v1/users')
      .set('Authorization', `Bearer ${token}`)
      .send({ username: `cashier-${suffix}`, displayName: 'كاشير الاختبار', password: 'cashier-password', roleId: managedRoleId, siteId })
      .expect(201);
    managedUserId = user.body.id as string;
    expect(user.body.passwordHash).toBeUndefined();
    expect(user.body.siteRoles[0].role.permissions).toEqual(['sales.create', 'stock.read']);

    const changed = await request(app.getHttpServer())
      .patch(`/api/v1/roles/${managedRoleId}`)
      .set('Authorization', `Bearer ${token}`)
      .send({ permissions: ['sales.create', 'sales.current_shift.read'] })
      .expect(200);
    expect(changed.body.permissions).toEqual(['sales.create', 'sales.current_shift.read']);
  });

  it('keeps café-specific current prices separate from historical invoice lines', async () => {
    const cafeSite = await request(app.getHttpServer())
      .post('/api/v1/sites')
      .set('Authorization', `Bearer ${token}`)
      .send({ code: `CAFE-SITE-${suffix}`, name: 'موقع كافيه اختبار', type: 'BRANCH_TYPE_2' })
      .expect(201);
    cafeSiteId = cafeSite.body.id as string;
    const customer = await request(app.getHttpServer())
      .post('/api/v1/admin/cafe/customers')
      .set('Authorization', `Bearer ${token}`)
      .send({ code: `CAFE-${suffix}`, name: 'كافيه اختبار', contact: 'test@example.invalid' })
      .expect(201);
    cafeCustomerId = customer.body.id as string;
    await request(app.getHttpServer())
      .put(`/api/v1/admin/cafe/customers/${cafeCustomerId}/prices/${itemId}`)
      .set('Authorization', `Bearer ${token}`)
      .send({ priceMinor: 32000 })
      .expect(200);

    const item = await prisma.item.findUniqueOrThrow({ where: { id: itemId } });
    const invoice = await prisma.cafeInvoice.create({
      data: {
        customerId: cafeCustomerId,
        issuingSiteId: cafeSiteId,
        invoiceNumber: `INV-${suffix}`,
        businessDate: new Date('2026-09-10T00:00:00.000Z'),
        netMinor: 64000n,
        occurredAt: new Date(),
        lines: {
          create: {
            itemId,
            quantityScaled: 2n,
            quantityScale: item.quantityScale,
            unitPriceMinor: 32000,
            totalMinor: 64000n,
            itemNameSnapshot: item.nameAr,
            skuSnapshot: item.sku,
            unitSnapshot: item.unit,
          },
        },
      },
    });
    cafeInvoiceId = invoice.id;
    await request(app.getHttpServer())
      .put(`/api/v1/admin/cafe/customers/${cafeCustomerId}/prices/${itemId}`)
      .set('Authorization', `Bearer ${token}`)
      .send({ priceMinor: 33000 })
      .expect(200);

    const overview = await request(app.getHttpServer())
      .get('/api/v1/admin/cafe/overview')
      .set('Authorization', `Bearer ${token}`)
      .expect(200);
    const accounts = (overview.body as {
      customers: Array<{
        id: string;
        prices: Array<{ price_minor: number }>;
        invoices: Array<{ lines: Array<{ unit_price_minor: number }> }>;
        outstanding_minor: string;
      }>;
    }).customers;
    const account = accounts.find((entry) => entry.id === cafeCustomerId);
    expect(account).toBeDefined();
    expect(account?.prices[0].price_minor).toBe(33000);
    expect(account?.invoices[0].lines[0].unit_price_minor).toBe(32000);
    expect(account?.outstanding_minor).toBe('64000');
  });
});

import { INestApplication } from '@nestjs/common';
import { Test } from '@nestjs/testing';
import * as argon2 from 'argon2';
import { randomUUID } from 'node:crypto';
import request = require('supertest');
import { AppModule } from '../src/app.module';
import { configureApp } from '../src/configure-app';
import { PrismaService } from '../src/prisma/prisma.service';
import { computeEventHash } from '../src/sync/sync.service';
import { hashSecret } from '../src/enrollment/enrollment.service';

describe('Contract v1 sync (PostgreSQL integration)', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let bearer: string;
  let deviceId: string;
  let deviceSecret: string;
  let siteId: string;
  let kitchenSiteId: string;
  let kitchenDeviceId: string;
  let kitchenDeviceSecret: string;
  let transferItemId: string;
  let ingredientItemId: string;
  let kitchenTestProductId: string;
  let kitchenTestIngredientId: string;
  let adminUserId: string;
  const username = `sync-admin-${randomUUID().slice(0, 8)}`;
  const siteCode = `SYNC-${randomUUID().slice(0, 8)}`.toUpperCase();
  const thumbprint = randomUUID().replaceAll('-', '').repeat(2);

  beforeAll(async () => {
    const moduleRef = await Test.createTestingModule({ imports: [AppModule] }).compile();
    app = moduleRef.createNestApplication();
    configureApp(app);
    await app.init();
    prisma = moduleRef.get(PrismaService);
    const role = await prisma.role.upsert({
      where: { code: 'ADMIN' },
      update: {},
      create: { code: 'ADMIN', name: 'Administrator', permissions: ['*'] },
    });
    const user = await prisma.user.create({
      data: {
        username,
        displayName: 'Sync test administrator',
        passwordHash: await argon2.hash('integration-password', { memoryCost: 4096, timeCost: 1 }),
      },
    });
    adminUserId = user.id;
    await prisma.userSiteRole.create({ data: { userId: user.id, roleId: role.id } });
    const login = await request(app.getHttpServer())
      .post('/api/v1/auth/login')
      .send({ username, password: 'integration-password' })
      .expect(201);
    bearer = login.body.access_token as string;
  }, 120_000);

  afterAll(async () => {
    const integrationSiteIds = [siteId, kitchenSiteId].filter((value): value is string => Boolean(value));
    if (integrationSiteIds.length) {
      await prisma.siteRetailPriceRevision.deleteMany({ where: { siteId: { in: integrationSiteIds } } });
      await prisma.siteRetailPrice.deleteMany({ where: { siteId: { in: integrationSiteIds } } });
    }
    if (siteId) {
      await prisma.saleCorrection.deleteMany({ where: { originalSale: { siteId } } });
      await prisma.retailSale.deleteMany({ where: { siteId } });
      await prisma.conflictLine.deleteMany({ where: { conflict: { siteId } } });
      await prisma.quantityConflict.deleteMany({ where: { siteId } });
      await prisma.kitchenIncomingReceiptLine.deleteMany({ where: { receipt: { shipment: { destinationSiteId: siteId } } } });
      await prisma.kitchenIncomingReceipt.deleteMany({ where: { shipment: { destinationSiteId: siteId } } });
      await prisma.kitchenShipmentLine.deleteMany({ where: { shipment: { destinationSiteId: siteId } } });
      await prisma.kitchenShipment.deleteMany({ where: { destinationSiteId: siteId } });
      await prisma.kitchenRequestLine.deleteMany({ where: { request: { requestingSiteId: siteId } } });
      await prisma.kitchenRequest.deleteMany({ where: { requestingSiteId: siteId } });
      await prisma.cafeItemPrice.deleteMany({ where: { customer: { originSiteId: siteId } } });
      await prisma.cafeCustomer.deleteMany({ where: { originSiteId: siteId } });
      await prisma.inventoryTransactionLine.deleteMany({ where: { transaction: { siteId } } });
      await prisma.inventoryTransaction.deleteMany({ where: { siteId } });
      await prisma.ingredientStockTransaction.deleteMany({ where: { siteId } });
      await prisma.stockBalance.deleteMany({ where: { siteId } });
    }
    if (kitchenSiteId) {
      await prisma.cafeInvoiceLine.deleteMany({ where: { invoice: { issuingSiteId: kitchenSiteId } } });
      await prisma.cafeInvoice.deleteMany({ where: { issuingSiteId: kitchenSiteId } });
      await prisma.cafePriceRevision.deleteMany({ where: { customer: { originSiteId: kitchenSiteId } } });
      await prisma.cafeItemPrice.deleteMany({ where: { customer: { originSiteId: kitchenSiteId } } });
      await prisma.cafeCustomer.deleteMany({ where: { originSiteId: kitchenSiteId } });
      await prisma.ingredientVariance.deleteMany({ where: { siteId: kitchenSiteId } });
      await prisma.ingredientStockTransaction.deleteMany({ where: { siteId: kitchenSiteId } });
      await prisma.stockBalance.deleteMany({ where: { siteId: kitchenSiteId } });
      await prisma.syncEvent.deleteMany({ where: { siteId: kitchenSiteId } });
      await prisma.enrollmentToken.deleteMany({ where: { siteId: kitchenSiteId } });
      await prisma.device.deleteMany({ where: { siteId: kitchenSiteId } });
      await prisma.site.delete({ where: { id: kitchenSiteId } });
    }
    if (siteId) {
      await prisma.syncEvent.deleteMany({ where: { siteId } });
      await prisma.enrollmentToken.deleteMany({ where: { siteId } });
      await prisma.device.deleteMany({ where: { siteId } });
      await prisma.site.delete({ where: { id: siteId } });
    }
    const user = await prisma.user.findUnique({ where: { username } });
    if (user) {
      await prisma.userSiteRole.deleteMany({ where: { userId: user.id } });
      await prisma.user.delete({ where: { id: user.id } });
    }
    const integrationItemIds = [transferItemId, ingredientItemId, kitchenTestProductId, kitchenTestIngredientId]
      .filter((value): value is string => Boolean(value));
    if (integrationItemIds.length) {
      await prisma.siteRetailPriceRevision.deleteMany({ where: { itemId: { in: integrationItemIds } } });
      await prisma.siteRetailPrice.deleteMany({ where: { itemId: { in: integrationItemIds } } });
      await prisma.retailPriceRevision.deleteMany({ where: { itemId: { in: integrationItemIds } } });
    }
    if (transferItemId) { await prisma.recipeComponent.deleteMany({ where: { recipe: { productItemId: transferItemId } } }); await prisma.recipe.deleteMany({ where: { productItemId: transferItemId } }); await prisma.item.delete({ where: { id: transferItemId } }); }
    if (ingredientItemId) await prisma.item.delete({ where: { id: ingredientItemId } });
    if (kitchenTestProductId) {
      await prisma.recipeComponent.deleteMany({ where: { recipe: { productItemId: kitchenTestProductId } } });
      await prisma.recipe.deleteMany({ where: { productItemId: kitchenTestProductId } });
    }
    if (kitchenTestProductId) await prisma.item.deleteMany({ where: { id: kitchenTestProductId } });
    if (kitchenTestIngredientId) await prisma.item.deleteMany({ where: { id: kitchenTestIngredientId } });
    await app.close();
  });

  it('enrolls exactly one profile-bound writer using a one-time token', async () => {
    const site = await request(app.getHttpServer())
      .post('/api/v1/sites')
      .set('Authorization', `Bearer ${bearer}`)
      .send({ code: siteCode, name: 'Sync Test Branch', type: 'BRANCH_TYPE_1' })
      .expect(201);
    siteId = site.body.id as string;
    const issued = await request(app.getHttpServer())
      .post(`/api/v1/sites/${siteId}/enrollment-tokens`)
      .set('Authorization', `Bearer ${bearer}`)
      .send({ expiresInMinutes: 30 })
      .expect(201);
    const enrolled = await request(app.getHttpServer())
      .post('/api/v1/enrollment')
      .send({
        token: issued.body.token,
        deviceName: 'Integration writer',
        keyThumbprint: thumbprint,
        appVersion: '1.0.0-test',
      })
      .expect(201);
    deviceId = enrolled.body.device.id as string;
    deviceSecret = enrolled.body.credential as string;
    expect(enrolled.body.device.credentialHash).toBeUndefined();

    await request(app.getHttpServer())
      .post('/api/v1/enrollment')
      .send({
        token: issued.body.token,
        deviceName: 'Duplicate writer',
        keyThumbprint: randomUUID().replaceAll('-', '').repeat(2),
        appVersion: '1.0.0-test',
      })
      .expect(401);
  });

  it('stores immutable events once and rejects hash, sequence, and dependency violations', async () => {
    const firstBase = {
      id: randomUUID(),
      device_sequence: 1,
      event_type: 'shift.opened',
      schema_version: 1,
      occurred_at: '2026-09-09T18:00:00.000Z',
      payload: { shift_id: randomUUID(), kind: 'MORNING', business_date: '2026-09-09' },
      dependencies: [] as string[],
    };
    const first = { ...firstBase, content_hash: computeEventHash(firstBase) };
    const headers = { 'x-device-id': deviceId, 'x-device-secret': deviceSecret };
    const push = (events: unknown[]) =>
      request(app.getHttpServer())
        .post('/api/v1/sync/push')
        .set(headers)
        .send({ contract_version: '1.0', stream_epoch: 1, events });

    const accepted = await push([first]).expect(200);
    expect(accepted.body.results[0].status).toBe('accepted');
    for (let replay = 0; replay < 10; replay += 1) {
      const duplicate = await push([first]).expect(200);
      expect(duplicate.body.results[0].status).toBe('duplicate');
    }

    const changedBase = { ...firstBase, payload: { ...firstBase.payload, kind: 'EVENING' } };
    await push([{ ...changedBase, content_hash: computeEventHash(changedBase) }])
      .expect(409)
      .expect((response) => expect(response.body.code).toBe('IDEMPOTENCY_KEY_REUSE'));

    const gapBase = { ...firstBase, id: randomUUID(), device_sequence: 3 };
    await push([{ ...gapBase, content_hash: computeEventHash(gapBase) }])
      .expect(409)
      .expect((response) => {
        expect(response.body.code).toBe('SEQUENCE_GAP');
        expect(response.body.retryable).toBe(true);
        expect(response.body.expected_sequence).toBe(2);
      });

    const dependencyBase = {
      ...firstBase,
      id: randomUUID(),
      device_sequence: 2,
      event_type: 'sale.completed',
      dependencies: [randomUUID()],
    };
    await push([{ ...dependencyBase, content_hash: computeEventHash(dependencyBase) }])
      .expect(409)
      .expect((response) => expect(response.body.code).toBe('DEPENDENCY_NOT_READY'));

    const saleProduct = await prisma.item.create({ data: { sku: `SALE-${randomUUID().slice(0, 8)}`, nameAr: 'Sale fixture', kind: 'PRODUCT', unit: 'pcs', quantityScale: 1 } });
    await prisma.stockBalance.create({ data: { siteId, itemId: saleProduct.id, location: 'SALEABLE', quantityScaled: 1n, asOfAt: new Date() } });
    const secondBase = { ...dependencyBase, dependencies: [first.id], payload: { sale_id: randomUUID(), receipt_number: 'SALE-TEST', business_date: new Date().toISOString().slice(0, 10),
      shift_kind: 'MORNING', payment_method: 'CASH', fulfillment: 'TAKEAWAY', subtotal_minor: 100, discount_minor: 0, tip_minor: 0, total_minor: 100,
      lines: [{ line_id: randomUUID(), item_id: saleProduct.id, quantity_scaled: 1, quantity_scale: 1, unit_price_minor: 100, allocated_discount_minor: 0, total_minor: 100 }] } };
    await push([{ ...secondBase, content_hash: computeEventHash(secondBase) }]).expect(200);
    await prisma.stockBalance.deleteMany({ where: { siteId, itemId: saleProduct.id } });
    await prisma.item.delete({ where: { id: saleProduct.id } });

    await request(app.getHttpServer()).get(`/api/v1/admin/sites/${siteId}/sales`).expect(401);
    const salesView = await request(app.getHttpServer())
      .get(`/api/v1/admin/sites/${siteId}/sales`)
      .set('Authorization', `Bearer ${bearer}`)
      .expect(200);
    const visibleSales = salesView.body as Array<{ event_id: string; site_id: string }>;
    expect(visibleSales.some((sale) => sale.event_id === secondBase.id && sale.site_id === siteId)).toBe(true);

    const pulled = await request(app.getHttpServer())
      .get('/api/v1/sync/pull?limit=100')
      .set(headers)
      .expect(200);
    // A writer has already committed its own events locally; the pull feed must
    // not echo them and accidentally apply their stock effects a second time.
    expect(pulled.body.events).toHaveLength(0);
    expect(pulled.body.has_more).toBe(false);
    await request(app.getHttpServer())
      .post('/api/v1/sync/ack')
      .set(headers)
      .send({ cursor: pulled.body.cursor })
      .expect(200);
  });

  it('routes an accepted branch request to the kitchen and exposes an admin-only read view', async () => {
    const kitchen = await request(app.getHttpServer())
      .post('/api/v1/sites')
      .set('Authorization', `Bearer ${bearer}`)
      .send({ code: `KIT-${randomUUID().slice(0, 8)}`.toUpperCase(), name: 'Integration Kitchen', type: 'KITCHEN' })
      .expect(201);
    kitchenSiteId = kitchen.body.id as string;
    const issued = await request(app.getHttpServer())
      .post(`/api/v1/sites/${kitchenSiteId}/enrollment-tokens`)
      .set('Authorization', `Bearer ${bearer}`)
      .send({ expiresInMinutes: 30 })
      .expect(201);
    const kitchenDevice = await request(app.getHttpServer())
      .post('/api/v1/enrollment')
      .send({
        token: issued.body.token,
        deviceName: 'Integration kitchen writer',
        keyThumbprint: randomUUID().replaceAll('-', '').repeat(2),
        appVersion: '1.0.0-test',
      })
      .expect(201);
    kitchenDeviceId = kitchenDevice.body.device.id as string;
    kitchenDeviceSecret = kitchenDevice.body.credential as string;

    const requestId = randomUUID();
    const requestLineId = randomUUID();
    const item = await prisma.item.create({ data: {
      sku: `SYNC-${randomUUID().slice(0, 8)}`, nameAr: 'Integration product', unit: 'pcs', quantityScale: 1,
      retailPriceMinor: 100, kind: 'PRODUCT',
    } });
    transferItemId = item.id;
    const ingredient = await prisma.item.create({ data: { sku: `ING-${randomUUID().slice(0, 8)}`, nameAr: 'Integration ingredient', unit: 'g', quantityScale: 1, kind: 'INGREDIENT' } });
    ingredientItemId = ingredient.id;
    await prisma.recipe.create({ data: { productItemId: item.id, outputScaled: 1n, components: { create: { ingredientItemId: ingredient.id, quantityScaled: 2n } } } });
    await prisma.stockBalance.create({ data: { siteId: kitchenSiteId, itemId: ingredient.id, location: 'KITCHEN', quantityScaled: 100n, asOfAt: new Date() } });
    const submitted = {
      id: randomUUID(),
      device_sequence: 3,
      event_type: 'kitchen_request.submitted',
      schema_version: 1,
      occurred_at: '2026-09-09T19:00:00.000Z',
      payload: { request_id: requestId, requesting_site_id: siteId, business_date: '2026-09-09', version: 1, lines: [{ line_id: requestLineId, item_id: item.id, requested_scaled: '10', quantity_scale: 1, name_snapshot: 'Integration product', unit_snapshot: 'pcs' }] },
      dependencies: [] as string[],
    };
    await request(app.getHttpServer())
      .post('/api/v1/sync/push')
      .set({ 'x-device-id': deviceId, 'x-device-secret': deviceSecret })
      .send({ contract_version: '1.0', stream_epoch: 1, events: [{ ...submitted, content_hash: computeEventHash(submitted) }] })
      .expect(200);

    const pulled = await request(app.getHttpServer())
      .get('/api/v1/sync/pull?limit=100')
      .set({ 'x-device-id': kitchenDevice.body.device.id, 'x-device-secret': kitchenDevice.body.credential })
      .expect(200);
    const kitchenEvents = pulled.body.events as Array<{ id: string; origin_site_id: string }>;
    expect(kitchenEvents.map((event) => event.id)).toContain(submitted.id);
    expect(kitchenEvents.find((event) => event.id === submitted.id)?.origin_site_id).toBe(siteId);

    const receivedByKitchen = {
      id: randomUUID(), device_sequence: 1, event_type: 'kitchen_request.received', schema_version: 1,
      occurred_at: '2026-09-09T19:01:00.1234567+00:00', dependencies: [submitted.id],
      payload: { request_id: requestId, destination_site_id: siteId, status: 'RECEIVED', version: 2,
        received_at: '2026-09-09T19:01:00.1234567+00:00' },
    };
    const kitchenHeaders = { 'x-device-id': kitchenDevice.body.device.id, 'x-device-secret': kitchenDevice.body.credential };
    await request(app.getHttpServer()).post('/api/v1/sync/push').set(kitchenHeaders)
      .send({ contract_version: '1.0', stream_epoch: 1,
        events: [{ ...receivedByKitchen, content_hash: computeEventHash(receivedByKitchen) }] }).expect(200);
    expect(await prisma.kitchenRequest.findUnique({ where: { id: requestId } })).toMatchObject({ status: 'RECEIVED', version: 2 });

    await request(app.getHttpServer()).get('/api/v1/admin/kitchen/requests').expect(401);
    const view = await request(app.getHttpServer())
      .get('/api/v1/admin/kitchen/requests')
      .set('Authorization', `Bearer ${bearer}`)
      .expect(200);
    const visibleRequests = view.body as Array<{ request: { request_id: string } }>;
    expect(visibleRequests.some((entry) => entry.request.request_id === requestId)).toBe(true);

    const shipmentId = randomUUID();
    const shipmentLineId = randomUUID();
    const dispatched = {
      id: randomUUID(), device_sequence: 2, event_type: 'shipment.dispatched', schema_version: 1,
      occurred_at: '2026-09-09T19:05:00.000Z', dependencies: [] as string[],
      payload: { shipment_id: shipmentId, request_id: requestId, destination_site_id: siteId, reference: 'KIT-TEST-1', version: 1, finalized: true,
        lines: [{ line_id: shipmentLineId, request_line_id: requestLineId, item_id: item.id, sent_scaled: '8' }],
        recipe_snapshot: [{ product_item_id: item.id, output_scaled: '8', recipe_version: 1, components: [{ ingredient_item_id: ingredient.id, quantity_scaled: '16' }] }],
        ingredient_lines: [{ ingredient_item_id: ingredient.id, quantity_scaled: '16' }] },
    };
    await request(app.getHttpServer()).post('/api/v1/sync/push').set(kitchenHeaders)
      .send({ contract_version: '1.0', stream_epoch: 1, events: [{ ...dispatched, content_hash: computeEventHash(dispatched) }] }).expect(200);
    expect(await prisma.kitchenRequest.findUnique({ where: { id: requestId } })).toMatchObject({ status: 'FULFILLED' });
    const branchPull = await request(app.getHttpServer()).get('/api/v1/sync/pull?limit=100')
      .set({ 'x-device-id': deviceId, 'x-device-secret': deviceSecret }).expect(200);
    const branchPullBody = branchPull.body as { events: { id: string; occurred_at: string; content_hash: string }[] };
    expect(branchPullBody.events.some((entry) => entry.id === receivedByKitchen.id)).toBe(true);
    expect(branchPullBody.events.some((entry) => entry.id === dispatched.id)).toBe(true);
    const pulledAcknowledgement = branchPullBody.events.find((entry) => entry.id === receivedByKitchen.id);
    expect(pulledAcknowledgement?.occurred_at).toBe(receivedByKitchen.occurred_at);
    expect(pulledAcknowledgement?.content_hash).toBe(computeEventHash(receivedByKitchen));

    const received = {
      id: randomUUID(), device_sequence: 4, event_type: 'incoming_receipt.accepted', schema_version: 1,
      occurred_at: '2026-09-09T19:10:00.000Z', dependencies: [] as string[],
      payload: { receipt_id: randomUUID(), shipment_id: shipmentId,
        lines: [{ receipt_line_id: randomUUID(), shipment_line_id: shipmentLineId, counted_scaled: '8' }] },
    };
    const branchHeaders = { 'x-device-id': deviceId, 'x-device-secret': deviceSecret };
    const acceptedReceipt = { ...received, content_hash: computeEventHash(received) };
    await request(app.getHttpServer()).post('/api/v1/sync/push').set(branchHeaders)
      .send({ contract_version: '1.0', stream_epoch: 1, events: [acceptedReceipt] }).expect(200);
    const replay = await request(app.getHttpServer()).post('/api/v1/sync/push').set(branchHeaders)
      .send({ contract_version: '1.0', stream_epoch: 1, events: [acceptedReceipt] }).expect(200);
    expect(replay.body.results[0].status).toBe('duplicate');
    expect((await prisma.kitchenIncomingReceipt.count({ where: { shipmentId } }))).toBe(1);

    const cafe = {
      id: randomUUID(), device_sequence: 5, event_type: 'cafe_customer.created', schema_version: 1,
      occurred_at: '2026-09-09T19:15:00.000Z', dependencies: [] as string[],
      payload: { customer_id: randomUUID(), site_id: siteId, name: 'Integration Cafe', kind: 'CAFE', phone: '01000000000', address: 'Cairo', version: 1,
        prices: [{ item_id: item.id, unit_price_minor: 150 }] },
    };
    await request(app.getHttpServer()).post('/api/v1/sync/push').set(branchHeaders)
      .send({ contract_version: '1.0', stream_epoch: 1, events: [{ ...cafe, content_hash: computeEventHash(cafe) }] }).expect(200);
    expect(await prisma.cafeCustomer.findUnique({ where: { id: cafe.payload.customer_id } })).toMatchObject({ originSiteId: siteId, name: 'Integration Cafe' });
  });

  it('runs the same request, shipment, and receipt protocol for a Branch Type 2 fixture', async () => {
    const branch = await prisma.site.create({ data: { code: `B2-${randomUUID().slice(0, 8)}`, name: 'Integration Branch 2', type: 'BRANCH_TYPE_2' } });
    const rawToken = `integration-${randomUUID()}`;
    const tokenHash = hashSecret(rawToken);
    await prisma.enrollmentToken.create({ data: { siteId: branch.id, profile: 'BRANCH_TYPE_2', tokenHash, expiresAt: new Date(Date.now() + 60_000), createdByUserId: adminUserId } });
    await request(app.getHttpServer()).post('/api/v1/enrollment').send({
      token: rawToken, deviceName: 'Wrong kitchen application', expectedProfile: 'KITCHEN',
      keyThumbprint: randomUUID().replaceAll('-', '').repeat(2), appVersion: 'test',
    }).expect(409);
    expect((await prisma.enrollmentToken.findUniqueOrThrow({ where: { tokenHash } })).usedAt).toBeNull();
    const enrolled = await request(app.getHttpServer()).post('/api/v1/enrollment').send({
      token: rawToken, deviceName: 'Branch 2 fixture', keyThumbprint: randomUUID().replaceAll('-', '').repeat(2), appVersion: 'test',
    }).expect(201);
    const headers = { 'x-device-id': enrolled.body.device.id, 'x-device-secret': enrolled.body.credential };
    const requestId = randomUUID(), requestLineId = randomUUID(), shipmentId = randomUUID(), shipmentLineId = randomUUID();
    const submitted = { id: randomUUID(), device_sequence: 1, event_type: 'kitchen_request.submitted', schema_version: 1,
      occurred_at: new Date().toISOString(), dependencies: [] as string[], payload: { request_id: requestId, requesting_site_id: branch.id, version: 1,
        lines: [{ line_id: requestLineId, item_id: transferItemId, requested_scaled: '3', quantity_scale: 1, name_snapshot: 'Integration product', unit_snapshot: 'pcs' }] } };
    await request(app.getHttpServer()).post('/api/v1/sync/push').set(headers).send({ contract_version: '1.0', stream_epoch: 1,
      events: [{ ...submitted, content_hash: computeEventHash(submitted) }] }).expect(200);
    const dispatched = { id: randomUUID(), device_sequence: 3, event_type: 'shipment.dispatched', schema_version: 1,
      occurred_at: new Date().toISOString(), dependencies: [] as string[], payload: { shipment_id: shipmentId, request_id: requestId,
        destination_site_id: branch.id, reference: 'KIT-B2-1', version: 1,
        lines: [{ line_id: shipmentLineId, request_line_id: requestLineId, item_id: transferItemId, sent_scaled: '3' }],
        recipe_snapshot: [{ product_item_id: transferItemId, output_scaled: '3', recipe_version: 1, components: [{ ingredient_item_id: ingredientItemId, quantity_scaled: '6' }] }],
        ingredient_lines: [{ ingredient_item_id: ingredientItemId, quantity_scaled: '6' }] } };
    await request(app.getHttpServer()).post('/api/v1/sync/push').set({ 'x-device-id': kitchenDeviceId, 'x-device-secret': kitchenDeviceSecret })
      .send({ contract_version: '1.0', stream_epoch: 1, events: [{ ...dispatched, content_hash: computeEventHash(dispatched) }] }).expect(200);
    const receipt = { id: randomUUID(), device_sequence: 2, event_type: 'incoming_receipt.accepted', schema_version: 1,
      occurred_at: new Date().toISOString(), dependencies: [] as string[], payload: { receipt_id: randomUUID(), shipment_id: shipmentId,
        lines: [{ receipt_line_id: randomUUID(), shipment_line_id: shipmentLineId, counted_scaled: '3' }] } };
    await request(app.getHttpServer()).post('/api/v1/sync/push').set(headers).send({ contract_version: '1.0', stream_epoch: 1,
      events: [{ ...receipt, content_hash: computeEventHash(receipt) }] }).expect(200);
    expect(await prisma.kitchenShipment.findUnique({ where: { id: shipmentId } })).toMatchObject({ destinationSiteId: branch.id, status: 'RECEIVED' });

    const grant = await request(app.getHttpServer()).post('/api/v1/auth/desktop-authorization').set(headers)
      .set('Authorization', `Bearer ${bearer}`).expect(201);
    let sequence = 3;
    async function inventory(kind: string, referenceId: string, stock: number, display = 0, occurredAt = new Date().toISOString()) {
      const eventId = randomUUID();
      const event = { id: eventId, device_sequence: sequence++, event_type: 'branch2.inventory.posted', schema_version: 1,
        occurred_at: occurredAt, dependencies: [] as string[], payload: {
          transaction_id: eventId, reference_id: referenceId, user_id: adminUserId, authorization: grant.body.authorization,
          kind, reason: 'Integration business operation', lines: [
            ...(stock ? [{ item_id: transferItemId, location: 'FREEZER', delta_scaled: String(stock) }] : []),
            ...(display ? [{ item_id: transferItemId, location: 'DISPLAY', delta_scaled: String(display) }] : []),
          ],
        } };
      const upload = { contract_version: '1.0', stream_epoch: 1, events: [{ ...event, content_hash: computeEventHash(event) }] };
      return { upload, response: await request(app.getHttpServer()).post('/api/v1/sync/push').set(headers).send(upload) };
    }
    expect((await inventory('IncomingReceipt', receipt.payload.receipt_id, 3)).response.status).toBe(200);
    const moved = await inventory('StockToDisplay', randomUUID(), -2, 2);
    expect(moved.response.status).toBe(200);
    await request(app.getHttpServer()).post('/api/v1/sync/push').set(headers).send(moved.upload).expect(200);
    expect(await prisma.stockBalance.findMany({ where: { siteId: branch.id } })).toEqual(expect.arrayContaining([
      expect.objectContaining({ location: 'FREEZER', quantityScaled: 1n }), expect.objectContaining({ location: 'DISPLAY', quantityScaled: 2n }),
    ]));
    const unexplained = await inventory('RetailSale', randomUUID(), 0, -1);
    expect(unexplained.response.status).toBe(409);
    sequence--;
    expect(await prisma.inventoryTransaction.count({ where: { siteId: branch.id } })).toBe(2);
    const saleId = randomUUID(), soldAt = new Date().toISOString();
    const cairoBusinessDate = new Intl.DateTimeFormat('en-CA', { timeZone: 'Africa/Cairo' }).format(new Date());
    const sale = { id: randomUUID(), device_sequence: sequence++, event_type: 'sale.completed', schema_version: 1,
      occurred_at: soldAt, dependencies: [] as string[], payload: { sale_id: saleId, receipt_number: 'B2-TEST-SALE',
        business_date: cairoBusinessDate, shift_kind: 'MORNING', payment_method: 'CASH', fulfillment: 'TAKEAWAY',
        subtotal_minor: 100, discount_minor: 0, tip_minor: 0, total_minor: 100,
        lines: [{ line_id: randomUUID(), item_id: transferItemId, quantity_scaled: 1, quantity_scale: 1,
          unit_price_minor: 100, allocated_discount_minor: 0, total_minor: 100, name_snapshot: 'Integration product', sku_snapshot: 'TEST', unit_snapshot: 'pcs' }] } };
    await request(app.getHttpServer()).post('/api/v1/sync/push').set(headers).send({ contract_version: '1.0', stream_epoch: 1,
      events: [{ ...sale, content_hash: computeEventHash(sale) }] }).expect(200);
    expect((await inventory('RetailSale', saleId, 0, -1, soldAt)).response.status).toBe(200);
    expect((await inventory('DisplayReturnToStock', randomUUID(), 1, -1)).response.status).toBe(200);
    const adminOverview = await request(app.getHttpServer()).get(`/api/v1/admin/sites/${branch.id}/overview`)
      .set('Authorization', `Bearer ${bearer}`).expect(200);
    expect(adminOverview.body.inventory_history).toHaveLength(4);
    expect(adminOverview.body.sales.today.net_minor).toBe('100');
    expect(adminOverview.body.sales.today.receipt_count).toBe(1);
    expect(adminOverview.body.inventory_history).toEqual(expect.arrayContaining([
      expect.objectContaining({ kind: 'RetailSale', reference_id: saleId, lines: [expect.objectContaining({ location: 'DISPLAY', delta_scaled: '-1' })] }),
    ]));
    expect(await prisma.stockBalance.findMany({ where: { siteId: branch.id } })).toEqual(expect.arrayContaining([
      expect.objectContaining({ location: 'FREEZER', quantityScaled: 2n }), expect.objectContaining({ location: 'DISPLAY', quantityScaled: 0n }),
    ]));
    await prisma.inventoryTransactionLine.deleteMany({ where: { transaction: { siteId: branch.id } } });
    await prisma.inventoryTransaction.deleteMany({ where: { siteId: branch.id } });
    await prisma.stockBalance.deleteMany({ where: { siteId: branch.id } });
    await prisma.retailSale.deleteMany({ where: { siteId: branch.id } });

    const cafeId = randomUUID();
    async function postBusiness(eventType: string, payload: Record<string, unknown>) {
      const event = { id: randomUUID(), device_sequence: sequence++, event_type: eventType, schema_version: 1,
        occurred_at: new Date().toISOString(), dependencies: [] as string[], payload };
      const upload = { contract_version: '1.0', stream_epoch: 1, events: [{ ...event, content_hash: computeEventHash(event) }] };
      await request(app.getHttpServer()).post('/api/v1/sync/push').set(headers).send(upload).expect(200);
      return upload;
    }
    await postBusiness('cafe_customer.created', { customer_id: cafeId, site_id: branch.id, name: 'Integration Cafe', phone: '01000000000', kind: 'Cafe', address: 'Test', prices: [] });
    const invoiceIds = [randomUUID(), randomUUID()];
    for (const invoiceId of invoiceIds) await postBusiness('custom_order.created', { custom_order_id: invoiceId, customer_id: cafeId, site_id: branch.id,
      order_number: `O-${invoiceId}`, total_minor: 100, status: 'NEW', version: 1,
      lines: [{ line_id: randomUUID(), item_id: transferItemId, quantity_scaled: 1, quantity_scale: 1, unit_price_minor: 100, line_total_minor: 100, item_name: 'Integration product', unit: 'pcs' }] });
    const paymentId = randomUUID();
    const paymentUpload = await postBusiness('custom_customer.payment_recorded', { payment_id: paymentId, source_custom_order_id: invoiceIds[0], customer_id: cafeId,
      site_id: branch.id, amount_minor: 150, payment_method: 'CASH', version: 2 });
    for (let repeat = 0; repeat < 10; repeat++) await request(app.getHttpServer()).post('/api/v1/sync/push').set(headers).send(paymentUpload).expect(200);
    expect(await prisma.cafePayment.count({ where: { customerId: cafeId } })).toBe(1);
    const allocation = await prisma.cafePaymentAllocation.aggregate({ where: { paymentId }, _sum: { amountMinor: true }, _count: true });
    expect(allocation._sum.amountMinor).toBe(150n);
    expect(allocation._count).toBe(2);
    const cafeOverview = await request(app.getHttpServer()).get('/api/v1/admin/cafe/overview').set('Authorization', `Bearer ${bearer}`).expect(200);
    const cafeOverviewBody = cafeOverview.body as { customers: { id: string; outstanding_minor: string }[] };
    expect(cafeOverviewBody.customers.find((customer) => customer.id === cafeId)?.outstanding_minor).toBe('50');
    await postBusiness('custom_order.status_changed', { custom_order_id: invoiceIds[1], status: 'CANCELLED', stock_lines: [], version: 2 });
    const cancelled = await prisma.cafeInvoice.findUniqueOrThrow({ where: { id: invoiceIds[1] } });
    expect(cancelled.status).toBe('REVERSED');
    expect(cancelled.netMinor).toBe(100n);
    expect(await prisma.cafePayment.count({ where: { customerId: cafeId } })).toBe(1);
    const afterCancellation = await request(app.getHttpServer()).get('/api/v1/admin/cafe/overview').set('Authorization', `Bearer ${bearer}`).expect(200);
    const afterCancellationBody = afterCancellation.body as { customers: { id: string; outstanding_minor: string; invoices: { id: string; outstanding_minor: string }[] }[] };
    const account = afterCancellationBody.customers.find((customer) => customer.id === cafeId);
    expect(account).toBeDefined();
    expect(account?.outstanding_minor).toBe('-50');
    expect(account?.invoices.find((invoice) => invoice.id === invoiceIds[1])?.outstanding_minor).toBe('0');
    await prisma.cafePaymentAllocation.deleteMany({ where: { paymentId } });
    await prisma.cafePayment.deleteMany({ where: { customerId: cafeId } });
    await prisma.cafeInvoiceLine.deleteMany({ where: { invoiceId: { in: invoiceIds } } });
    await prisma.cafeInvoice.deleteMany({ where: { customerId: cafeId } });
    await prisma.cafeCustomer.delete({ where: { id: cafeId } });

    const sharedItemId = randomUUID();
    const branchTwoCatalogUpload = await postBusiness('catalog.item.updated', {
      item_id: sharedItemId, site_id: branch.id, sku: `SHARED-${sharedItemId.slice(0, 8)}`,
      name_ar: 'Shared catalog product', unit: 'pcs', quantity_scale: 1,
      retail_price_minor: 12_500, kind: 'PRODUCT', active: true, version: 1,
    });
    const branchOneCatalogEvent = {
      id: randomUUID(), device_sequence: 6, event_type: 'catalog.item.updated', schema_version: 1,
      occurred_at: new Date().toISOString(), dependencies: [] as string[], payload: {
        item_id: sharedItemId, site_id: siteId, sku: `SHARED-${sharedItemId.slice(0, 8)}`,
        name_ar: 'Shared catalog product', unit: 'pcs', quantity_scale: 1,
        retail_price_minor: 10_000, kind: 'PRODUCT', active: true, version: 2,
      },
    };
    await request(app.getHttpServer()).post('/api/v1/sync/push')
      .set({ 'x-device-id': deviceId, 'x-device-secret': deviceSecret })
      .send({ contract_version: '1.0', stream_epoch: 1,
        events: [{ ...branchOneCatalogEvent, content_hash: computeEventHash(branchOneCatalogEvent) }] }).expect(200);

    const prices = await prisma.siteRetailPrice.findMany({ where: { itemId: sharedItemId }, orderBy: { priceMinor: 'asc' } });
    expect(prices).toEqual([
      expect.objectContaining({ siteId, priceMinor: 10_000 }),
      expect.objectContaining({ siteId: branch.id, priceMinor: 12_500 }),
    ]);
    const branchOneBootstrap = await request(app.getHttpServer()).get('/api/v1/sync/bootstrap')
      .set({ 'x-device-id': deviceId, 'x-device-secret': deviceSecret }).expect(200);
    const branchTwoBootstrap = await request(app.getHttpServer()).get('/api/v1/sync/bootstrap').set(headers).expect(200);
    const branchOneCatalog = (branchOneBootstrap.body as { catalog: { id: string; retailPriceMinor: number }[] }).catalog;
    const branchTwoCatalog = (branchTwoBootstrap.body as { catalog: { id: string; retailPriceMinor: number }[] }).catalog;
    expect(branchOneCatalog.find((row) => row.id === sharedItemId)?.retailPriceMinor).toBe(10_000);
    expect(branchTwoCatalog.find((row) => row.id === sharedItemId)?.retailPriceMinor).toBe(12_500);
    const kitchenPull = await request(app.getHttpServer()).get('/api/v1/sync/pull?limit=100')
      .set({ 'x-device-id': kitchenDeviceId, 'x-device-secret': kitchenDeviceSecret }).expect(200);
    const kitchenEvents = (kitchenPull.body as { events: { id: string }[] }).events;
    expect(kitchenEvents.some((row) => row.id === branchTwoCatalogUpload.events[0].id)).toBe(true);

    await prisma.siteRetailPriceRevision.deleteMany({ where: { itemId: sharedItemId } });
    await prisma.siteRetailPrice.deleteMany({ where: { itemId: sharedItemId } });
    await prisma.retailPriceRevision.deleteMany({ where: { itemId: sharedItemId } });
    await prisma.item.delete({ where: { id: sharedItemId } });

    await prisma.kitchenIncomingReceiptLine.deleteMany({ where: { receipt: { shipmentId } } });
    await prisma.kitchenIncomingReceipt.deleteMany({ where: { shipmentId } });
    await prisma.kitchenShipmentLine.deleteMany({ where: { shipmentId } });
    await prisma.kitchenShipment.delete({ where: { id: shipmentId } });
    await prisma.kitchenRequestLine.deleteMany({ where: { requestId } });
    await prisma.kitchenRequest.delete({ where: { id: requestId } });
    await prisma.syncEvent.deleteMany({ where: { siteId: branch.id } });
    await prisma.siteRetailPriceRevision.deleteMany({ where: { siteId: branch.id } });
    await prisma.siteRetailPrice.deleteMany({ where: { siteId: branch.id } });
    await prisma.enrollmentToken.deleteMany({ where: { siteId: branch.id } });
    await prisma.device.deleteMany({ where: { siteId: branch.id } });
    await prisma.site.delete({ where: { id: branch.id } });
  });

  it('posts kitchen receiving, waste, and immutable daily counts exactly once', async () => {
    const headers = { 'x-device-id': kitchenDeviceId, 'x-device-secret': kitchenDeviceSecret };
    const post = async (sequence: number, eventType: string, payload: Record<string, unknown>) => {
      const event = { id: randomUUID(), device_sequence: sequence, event_type: eventType, schema_version: 1,
        occurred_at: new Date().toISOString(), dependencies: [] as string[], payload };
      const upload = { contract_version: '1.0', stream_epoch: 1, events: [{ ...event, content_hash: computeEventHash(event) }] };
      const response = await request(app.getHttpServer()).post('/api/v1/sync/push').set(headers).send(upload).expect(200);
      return { event, upload, response };
    };
    const received = await post(4, 'ingredient.received', { operation_id: randomUUID(), reason: 'Supplier delivery',
      lines: [{ line_id: randomUUID(), item_id: ingredientItemId, quantity_scaled: '10' }] });
    await request(app.getHttpServer()).post('/api/v1/sync/push').set(headers).send(received.upload).expect(200);
    expect((await prisma.stockBalance.findFirstOrThrow({ where: { siteId: kitchenSiteId, itemId: ingredientItemId, location: 'KITCHEN' } })).quantityScaled).toBe(88n);
    await post(5, 'ingredient.waste', { operation_id: randomUUID(), reason: 'Damaged ingredient',
      lines: [{ line_id: randomUUID(), item_id: ingredientItemId, quantity_scaled: '4' }] });
    const countLineId = randomUUID();
    await post(6, 'ingredient.counted', { count_id: randomUUID(), business_date: new Date().toISOString().slice(0, 10),
      lines: [{ line_id: countLineId, item_id: ingredientItemId, actual_scaled: '78', recorded_waste_scaled: '4' }] });
    expect((await prisma.stockBalance.findFirstOrThrow({ where: { siteId: kitchenSiteId, itemId: ingredientItemId, location: 'KITCHEN' } })).quantityScaled).toBe(78n);
    expect(await prisma.ingredientVariance.findUnique({ where: { id: countLineId } })).toMatchObject({ expectedScaled: 84n, actualScaled: 78n, recordedWasteScaled: 4n, unexplainedVarianceScaled: 6n });
  });

  it('syncs kitchen catalog, recipe, cafe, purchase cost and one cafe fulfillment', async () => {
    const headers = { 'x-device-id': kitchenDeviceId, 'x-device-secret': kitchenDeviceSecret };
    const post = async (sequence: number, eventType: string, payload: Record<string, unknown>) => {
      const event = { id: randomUUID(), device_sequence: sequence, event_type: eventType, schema_version: 1,
        occurred_at: new Date().toISOString(), dependencies: [] as string[], payload };
      const upload = { contract_version: '1.0', stream_epoch: 1, events: [{ ...event, content_hash: computeEventHash(event) }] };
      const response = await request(app.getHttpServer()).post('/api/v1/sync/push').set(headers).send(upload).expect(200);
      expect((response.body as { results: { status: string }[] }).results[0].status).toBe('accepted');
      return upload;
    };
    kitchenTestProductId = randomUUID(); kitchenTestIngredientId = randomUUID();
    await post(7, 'catalog.item.updated', { item_id: kitchenTestIngredientId, site_id: kitchenSiteId,
      sku: `AUTO-${kitchenTestIngredientId}`, name_ar: 'Integration Flour', unit: 'g', quantity_scale: 1,
      retail_price_minor: 0, kind: 'INGREDIENT', active: true, version: 1 });
    await post(8, 'catalog.item.updated', { item_id: kitchenTestProductId, site_id: kitchenSiteId,
      sku: `AUTO-${kitchenTestProductId}`, name_ar: 'Integration Cake', unit: 'piece', quantity_scale: 1,
      retail_price_minor: 50_000, kind: 'PRODUCT', active: true, version: 1 });
    await post(9, 'recipe.updated', { product_item_id: kitchenTestProductId, output_scaled: '1', version: 1,
      components: [{ ingredient_item_id: kitchenTestIngredientId, quantity_scaled: '500' }] });
    await post(10, 'ingredient.received', { operation_id: randomUUID(), reason: 'Supplier purchase',
      lines: [{ line_id: randomUUID(), item_id: kitchenTestIngredientId, quantity_scaled: '1000', total_cost_minor: '50000' }] });
    const cafeId = randomUUID();
    await post(11, 'cafe_customer.created', { customer_id: cafeId, site_id: kitchenSiteId,
      name: 'Integration Cafe', phone: '01000000000', kind: 'Cafe', version: 1,
      prices: [{ item_id: kitchenTestProductId, unit_price_minor: 50_000 }] });
    const invoiceId = randomUUID();
    await post(12, 'custom_order.created', { custom_order_id: invoiceId, customer_id: cafeId, site_id: kitchenSiteId,
      order_number: `K-CF-${invoiceId.slice(0, 8)}`, total_minor: 50_000, status: 'NEW', version: 1,
      lines: [{ line_id: randomUUID(), item_id: kitchenTestProductId, item_name: 'Integration Cake', unit: 'piece',
        quantity_scale: 1, quantity_scaled: '1', unit_price_minor: 50_000, line_total_minor: 50_000 }] });
    const fulfillment = await post(13, 'custom_order.status_changed', { custom_order_id: invoiceId,
      site_id: kitchenSiteId, status: 'DELIVERED', version: 2,
      stock_lines: [{ item_id: kitchenTestProductId, quantity_scaled: '1' }],
      recipe_snapshot: [{ product_item_id: kitchenTestProductId, output_scaled: '1', recipe_version: 1,
        components: [{ ingredient_item_id: kitchenTestIngredientId, quantity_scaled: '500' }] }],
      ingredient_lines: [{ line_id: randomUUID(), ingredient_item_id: kitchenTestIngredientId,
        quantity_scaled: '500', cost_minor: '25000' }], production_cost_minor: '25000' });
    const replay = await request(app.getHttpServer()).post('/api/v1/sync/push').set(headers).send(fulfillment).expect(200);
    expect((replay.body as { results: { status: string }[] }).results[0].status).toBe('duplicate');
    const balance = await prisma.stockBalance.findFirstOrThrow({ where: { siteId: kitchenSiteId,
      itemId: kitchenTestIngredientId, location: 'KITCHEN' } });
    expect(balance.quantityScaled).toBe(500n);
    expect(balance.inventoryCostMinor).toBe(25_000n);
    expect(await prisma.ingredientStockTransaction.count({ where: { siteId: kitchenSiteId,
      itemId: kitchenTestIngredientId, kind: 'CAFE_PRODUCTION' } })).toBe(1);
    expect((await prisma.cafeInvoice.findUniqueOrThrow({ where: { id: invoiceId } })).fulfillmentCostMinor).toBe(25_000n);
    const kitchenBootstrap = await request(app.getHttpServer()).get('/api/v1/sync/bootstrap').set(headers).expect(200);
    const branchBootstrap = await request(app.getHttpServer()).get('/api/v1/sync/bootstrap')
      .set({ 'x-device-id': deviceId, 'x-device-secret': deviceSecret }).expect(200);
    const kitchenBody = kitchenBootstrap.body as { catalog: { id: string }[]; recipes: { product_item_id: string }[];
      customers: { id: string }[]; stock: { itemId: string; quantityScaled: string; inventoryCostMinor: string }[] };
    const branchBody = branchBootstrap.body as { catalog: { id: string }[] };
    expect(kitchenBody.catalog.some((row) => row.id === kitchenTestProductId)).toBe(true);
    expect(kitchenBody.recipes.some((row) => row.product_item_id === kitchenTestProductId)).toBe(true);
    expect(kitchenBody.customers.some((row) => row.id === cafeId)).toBe(true);
    expect(kitchenBody.stock.find((row) => row.itemId === kitchenTestIngredientId)).toMatchObject({
      quantityScaled: '500', inventoryCostMinor: '25000',
    });
    expect(branchBody.catalog.some((row) => row.id === kitchenTestProductId)).toBe(true);
  });
});

import { INestApplication } from '@nestjs/common';
import { Test } from '@nestjs/testing';
import * as argon2 from 'argon2';
import { randomUUID } from 'node:crypto';
import request = require('supertest');
import { AppModule } from '../src/app.module';
import { configureApp } from '../src/configure-app';
import { PrismaService } from '../src/prisma/prisma.service';
import { computeEventHash } from '../src/sync/sync.service';

describe('Contract v1 sync (PostgreSQL integration)', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let bearer: string;
  let deviceId: string;
  let deviceSecret: string;
  let siteId: string;
  let kitchenSiteId: string;
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
    await prisma.userSiteRole.create({ data: { userId: user.id, roleId: role.id } });
    const login = await request(app.getHttpServer())
      .post('/api/v1/auth/login')
      .send({ username, password: 'integration-password' })
      .expect(201);
    bearer = login.body.access_token as string;
  }, 120_000);

  afterAll(async () => {
    if (kitchenSiteId) {
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

    const secondBase = { ...dependencyBase, dependencies: [first.id] };
    await push([{ ...secondBase, content_hash: computeEventHash(secondBase) }]).expect(200);

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
    expect(pulled.body.events).toHaveLength(2);
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

    const requestId = randomUUID();
    const submitted = {
      id: randomUUID(),
      device_sequence: 3,
      event_type: 'kitchen_request.submitted',
      schema_version: 1,
      occurred_at: '2026-09-09T19:00:00.000Z',
      payload: { request_id: requestId, business_date: '2026-09-09', version: 1, lines: [] },
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

    await request(app.getHttpServer()).get('/api/v1/admin/kitchen/requests').expect(401);
    const view = await request(app.getHttpServer())
      .get('/api/v1/admin/kitchen/requests')
      .set('Authorization', `Bearer ${bearer}`)
      .expect(200);
    const visibleRequests = view.body as Array<{ request: { request_id: string } }>;
    expect(visibleRequests.some((entry) => entry.request.request_id === requestId)).toBe(true);
  });
});

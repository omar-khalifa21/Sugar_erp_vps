import { INestApplication } from '@nestjs/common';
import { JwtService } from '@nestjs/jwt';
import { Test } from '@nestjs/testing';
import * as argon2 from 'argon2';
import { randomBytes, randomUUID } from 'node:crypto';
import request = require('supertest');
import { AppModule } from '../src/app.module';
import { configureApp } from '../src/configure-app';
import { PrismaService } from '../src/prisma/prisma.service';

describe('Kitchen password enrollment', () => {
  let app: INestApplication;
  let db: PrismaService;
  let siteId: string;
  let admin: string;
  let staff: string;
  const actors: string[] = [];
  const setting = '/api/v1/admin/kitchen-enrollment';
  const password = randomBytes(20).toString('hex');
  const rotated = randomBytes(20).toString('hex');
  const api = () => request(app.getHttpServer());
  const enroll = (value: string, address: string) => api().post('/api/v1/enrollment/kitchen')
    .set('X-Forwarded-Proto', 'https').set('X-Forwarded-For', address).send({
      password: value, deviceName: 'Kitchen PC', keyThumbprint: randomBytes(32).toString('hex'), appVersion: '1.0.0',
    });
  const save = (value: string) => api().put(setting).auth(admin, { type: 'bearer' })
    .set('X-Forwarded-Proto', 'https').send({ siteId, password: value });

  beforeAll(async () => {
    const module = await Test.createTestingModule({ imports: [AppModule] }).compile();
    app = module.createNestApplication({ bodyParser: false });
    configureApp(app);
    app.getHttpAdapter().getInstance().set('trust proxy', 'loopback');
    await app.init();
    db = module.get(PrismaService);
    if (await db.kitchenEnrollmentSetting.count()) throw new Error('Kitchen enrollment tests require an unconfigured test database');
    const role = await db.role.upsert({ where: { code: 'ADMIN' }, update: {},
      create: { code: 'ADMIN', name: 'Administrator', permissions: ['*'] } });
    for (const privileged of [true, false]) {
      const user = await db.user.create({ data: { username: randomUUID(), displayName: 'Test user',
        passwordHash: await argon2.hash(randomBytes(32)), ...(privileged ? { siteRoles: { create: { roleId: role.id } } } : {}) } });
      actors.push(user.id);
      const token = await module.get(JwtService).signAsync({ sub: user.id });
      if (privileged) admin = token; else staff = token;
    }
    siteId = (await db.site.create({ data: { name: 'Test kitchen', code: randomUUID(), type: 'KITCHEN' } })).id;
  });

  afterAll(async () => {
    if (siteId) {
      await db.kitchenEnrollmentSetting.deleteMany({ where: { siteId } });
      await db.device.deleteMany({ where: { siteId } });
      await db.site.delete({ where: { id: siteId } });
    }
    if (actors.length) {
      await db.userSiteRole.deleteMany({ where: { userId: { in: actors } } });
      await db.user.deleteMany({ where: { id: { in: actors } } });
    }
    await app?.close();
  });

  it('requires an administrator and HTTPS to set a password', async () => {
    await api().put(setting).send({ siteId, password }).expect(401);
    await api().put(setting).auth(staff, { type: 'bearer' }).send({ siteId, password }).expect(403);
    await api().put(setting).auth(admin, { type: 'bearer' }).send({ siteId, password }).expect(400);
    await api().get(setting).auth(staff, { type: 'bearer' }).expect(403);
    await api().post('/api/v1/enrollment/kitchen').send({ password, deviceName: 'Kitchen', appVersion: '1',
      keyThumbprint: randomBytes(32).toString('hex') }).expect(400);
  });

  it('limits enrollment attempts independently of device sync', async () => {
    for (let i = 0; i < 5; i++) await enroll('incorrect', '192.0.2.10').expect(401);
    await enroll('incorrect', '192.0.2.10').expect(429);
  });

  it('rotates the password, preserves an enrolled writer, and revokes its device credential', async () => {
    const result = await save(password).expect(200);
    expect(Object.keys(result.body).sort()).toEqual(['siteId', 'updatedAt']);
    const row = await db.kitchenEnrollmentSetting.findUniqueOrThrow({ where: { id: 1 } });
    expect(row.passwordHash).toMatch(/^\$argon2id\$/);
    expect(await argon2.verify(row.passwordHash, password)).toBe(true);
    const race = await Promise.all([enroll(password, '192.0.2.11'), enroll(password, '192.0.2.12')]);
    expect(race.map(response => response.status).sort()).toEqual([201, 409]);
    const { device, credential } = race.find(response => response.status === 201)!.body;
    expect(device.siteId).toBe(siteId);
    expect(device.profile).toBe('KITCHEN');
    const bootstrap = () => api().get('/api/v1/sync/bootstrap').set('X-Device-Id', device.id).set('X-Device-Secret', credential);
    await save(rotated).expect(200);
    await enroll(password, '192.0.2.13').expect(401);
    await bootstrap().expect(200);
    await api().post('/api/v1/devices/' + device.id + '/revoke').auth(admin, { type: 'bearer' }).expect(201);
    await bootstrap().expect(401);
    const next = await enroll(rotated, '192.0.2.14').expect(201);
    expect(next.body.credential).not.toBe(credential);
    expect(next.body.device.id).not.toBe(device.id);
  });
});

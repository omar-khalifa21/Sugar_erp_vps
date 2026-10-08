import { INestApplication } from '@nestjs/common';
import { JwtService } from '@nestjs/jwt';
import { Test } from '@nestjs/testing';
import * as argon2 from 'argon2';
import { randomUUID } from 'node:crypto';
import request = require('supertest');
import { AppModule } from '../src/app.module';
import { configureApp } from '../src/configure-app';
import { PrismaService } from '../src/prisma/prisma.service';

describe('Account security (isolated PostgreSQL)', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let jwt: JwtService;
  let admin: { id: string; username: string };
  let roleId: string;
  let operatorRoleId: string;
  let token: string;
  let passwordHash: string;
  const password = 'synthetic-password-123';
  const suffix = randomUUID().slice(0, 8);
  const bearer = () => ({ Authorization: `Bearer ${token}` });
  const account = async (status = 'ACTIVE', isAdmin = false) => prisma.user.create({ data: {
    username: `security-${randomUUID()}`, displayName: 'Synthetic account', passwordHash, status,
    ...(status === 'ACTIVE' ? { siteRoles: { create: { roleId: isAdmin ? roleId : operatorRoleId } } } : {}),
  } });

  beforeAll(async () => {
    const database = new URL(process.env.DATABASE_URL ?? 'http://unset');
    if (database.pathname !== '/sugar_security_test') throw new Error('Run account security tests only against the disposable sugar_security_test database');
    const module = await Test.createTestingModule({ imports: [AppModule] }).compile();
    app = module.createNestApplication({ bodyParser: false });
    configureApp(app);
    await app.init();
    prisma = module.get(PrismaService);
    jwt = module.get(JwtService);
    passwordHash = await argon2.hash(password);
    const role = await prisma.role.upsert({ where: { code: 'ADMIN' }, update: { active: true }, create: { code: 'ADMIN', name: 'Admin', permissions: ['*'] } });
    roleId = role.id;
    operatorRoleId = (await prisma.role.create({ data: { code: `TEST_${suffix.toUpperCase()}`, name: 'Test operator', permissions: ['inventory.write'] } })).id;
    admin = await account('ACTIVE', true);
    token = await jwt.signAsync({ sub: admin.id, ver: 0 });
  });
  afterAll(async () => {
    // The audit is intentionally append-only; use only the disposable test DB.
    await app.close();
  });

  it('requires approval through login, bearer middleware and desktop authorization, including old tokens', async () => {
    const username = `signup-${suffix}`;
    await request(app.getHttpServer()).post('/api/v1/auth/signup').send({ username, displayName: 'Signup', password }).expect(201).expect({ status: 'PENDING_PERMISSION' });
    const pending = await prisma.user.findUniqueOrThrow({ where: { username } });
    const login = await request(app.getHttpServer()).post('/api/v1/auth/login').send({ username, password }).expect(403);
    expect(login.body.code).toBe('ACCOUNT_PENDING');
    expect(login.body.access_token).toBeUndefined();
    const oldToken = await jwt.signAsync({ sub: pending.id });
    for (const path of ['/auth/me', '/users', '/sites', '/admin/reports']) {
      await request(app.getHttpServer()).get(`/api/v1${path}`).set('Authorization', `Bearer ${oldToken}`).expect(401);
    }
    await request(app.getHttpServer()).post('/api/v1/auth/desktop-authorization').set('Authorization', `Bearer ${oldToken}`).expect(401);
    await request(app.getHttpServer()).post(`/api/v1/users/${pending.id}/approve`).set(bearer()).send({ roleId: operatorRoleId }).expect(201);
    await request(app.getHttpServer()).post(`/api/v1/users/${pending.id}/approve`).set(bearer()).send({ roleId: operatorRoleId }).expect(409);
    const approved = await request(app.getHttpServer()).post('/api/v1/auth/login').send({ username, password }).expect(201);
    await request(app.getHttpServer()).get('/api/v1/auth/me').set('Authorization', `Bearer ${approved.body.access_token}`).expect(200);
    await request(app.getHttpServer()).get('/api/v1/auth/me').set('Authorization', `Bearer ${oldToken}`).expect(401);
    expect(await prisma.userAuditEvent.count({ where: { targetId: pending.id, action: 'USER_APPROVED' } })).toBe(1);
  });

  it('gives identical duplicate-signup responses and generic errors without valid credentials', async () => {
    const input = { username: `duplicate-${suffix}`, displayName: 'Duplicate', password };
    const first = await request(app.getHttpServer()).post('/api/v1/auth/signup').send(input).expect(201);
    const duplicate = await request(app.getHttpServer()).post('/api/v1/auth/signup').send({ ...input, password: 'another-synthetic-password' }).expect(201);
    expect(first.body).toEqual(duplicate.body);
    const unknown = await request(app.getHttpServer()).post('/api/v1/auth/login').send({ username: 'missing-account', password }).expect(401);
    const wrong = await request(app.getHttpServer()).post('/api/v1/auth/login').send({ username: input.username, password: 'wrong-password' }).expect(401);
    expect(unknown.body.message).toBe(wrong.body.message);
  });

  it('rejects anonymous/non-admin approve, reject and permanent-delete calls', async () => {
    const user = await account();
    const target = await account('PENDING_PERMISSION');
    const ordinaryToken = await jwt.signAsync({ sub: user.id, ver: 0 });
    for (const [method, action, body] of [
      ['post', 'approve', { roleId: operatorRoleId }], ['post', 'reject', { confirmation: target.username }], ['delete', 'permanent', { confirmation: target.username }],
    ] as const) {
      const path = `/api/v1/users/${target.id}/${action}`;
      await request(app.getHttpServer())[method](path).send(body).expect(401);
      await request(app.getHttpServer())[method](path).set('Authorization', `Bearer ${ordinaryToken}`).send(body).expect(403);
    }
    expect(await prisma.user.count({ where: { id: target.id } })).toBe(1);
  });

  it('hard-deletes dependent credentials/roles, retains business attribution and an immutable audit, revokes tokens', async () => {
    const target = await account();
    const targetToken = await jwt.signAsync({ sub: target.id, ver: 0 });
    const site = await prisma.site.create({ data: { code: `SEC_${suffix.toUpperCase()}`, name: 'Security fixture', type: 'BRANCH_TYPE_1' } });
    const item = await prisma.item.create({ data: { sku: `SEC_${suffix}`, nameAr: 'Synthetic item', unit: 'piece', quantityScale: 1, kind: 'PRODUCT' } });
    const adjustment = await prisma.stockAdjustment.create({ data: { siteId: site.id, itemId: item.id, location: 'SALEABLE', deltaScaled: 1n, reason: 'Synthetic fixture', expectedVersion: 1, createdByUserId: target.id } });
    const inventory = await prisma.inventoryTransaction.create({ data: { id: randomUUID(), siteId: site.id, userId: target.id,
      referenceId: randomUUID(), kind: 'IncomingReceipt', reason: 'Synthetic fixture', occurredAt: new Date(), sourceEventId: randomUUID(),
      lines: { create: { itemId: item.id, location: 'SALEABLE', deltaScaled: 1n } } }, include: { lines: true } });
    const conflict = await prisma.quantityConflict.create({ data: { siteId: site.id, reference: `SEC_${suffix}`,
      sentAt: new Date(), countedAt: new Date() } });
    const decision = await prisma.adminDecision.create({ data: { conflictId: conflict.id, adminUserId: target.id,
      expectedVersion: 1, reason: 'Synthetic fixture' } });
    await prisma.enrollmentToken.create({ data: { siteId: site.id, profile: 'BRANCH_TYPE_1', tokenHash: 'a'.repeat(64), expiresAt: new Date(Date.now() + 60_000), createdByUserId: target.id } });
    await request(app.getHttpServer()).delete(`/api/v1/users/${target.id}/permanent`).set(bearer()).send({ confirmation: 'wrong' }).expect(400);
    expect(await prisma.userAuditEvent.count({ where: { targetId: target.id } })).toBe(0);
    await request(app.getHttpServer()).delete(`/api/v1/users/${target.id}/permanent`).set(bearer()).send({ confirmation: target.username }).expect(200);
    expect(await prisma.user.findUnique({ where: { id: target.id } })).toBeNull();
    expect(await prisma.userSiteRole.count({ where: { userId: target.id } })).toBe(0);
    expect(await prisma.enrollmentToken.count({ where: { createdByUserId: target.id } })).toBe(0);
    expect(await prisma.stockAdjustment.findUnique({ where: { id: adjustment.id } })).toEqual(adjustment);
    expect(await prisma.inventoryTransaction.findUnique({ where: { id: inventory.id }, include: { lines: true } })).toEqual(inventory);
    expect(await prisma.adminDecision.findUnique({ where: { id: decision.id } })).toEqual(decision);
    await request(app.getHttpServer()).get(`/api/v1/admin/sites/${site.id}/overview`).set(bearer()).expect(200);
    await request(app.getHttpServer()).get('/api/v1/admin/conflicts').set(bearer()).expect(200);
    expect(await prisma.userIdentity.findUnique({ where: { id: target.id } })).toMatchObject({ displayName: target.displayName });
    const audit = await prisma.userAuditEvent.findFirstOrThrow({ where: { targetId: target.id } });
    expect(audit).toMatchObject({ action: 'USER_DELETED', actorId: admin.id, actorUsername: admin.username, targetUsername: target.username });
    await expect(prisma.userAuditEvent.delete({ where: { id: audit.id } })).rejects.toThrow();
    await request(app.getHttpServer()).get('/api/v1/auth/me').set('Authorization', `Bearer ${targetToken}`).expect(401);
    await request(app.getHttpServer()).delete(`/api/v1/users/${target.id}/permanent`).set(bearer()).send({ confirmation: target.username }).expect(404);
  });

  it('rejects only pending accounts and removes their unused identity', async () => {
    const target = await account('PENDING_PERMISSION');
    await request(app.getHttpServer()).post(`/api/v1/users/${target.id}/reject`).set(bearer()).send({ confirmation: target.username }).expect(201);
    expect(await prisma.user.findUnique({ where: { id: target.id } })).toBeNull();
    expect(await prisma.userIdentity.findUnique({ where: { id: target.id } })).toBeNull();
    await request(app.getHttpServer()).post('/api/v1/auth/login').send({ username: target.username, password }).expect(401);
    const approved = await account();
    await request(app.getHttpServer()).post(`/api/v1/users/${approved.id}/reject`).set(bearer()).send({ confirmation: approved.username }).expect(409);
  });

  it('revokes old tokens after password reset without changing approval', async () => {
    const user = await account();
    const oldToken = await jwt.signAsync({ sub: user.id, ver: 0 });
    await request(app.getHttpServer()).patch(`/api/v1/users/${user.id}`).set(bearer()).send({ password: 'replacement-password-123' }).expect(200);
    await request(app.getHttpServer()).get('/api/v1/auth/me').set('Authorization', `Bearer ${oldToken}`).expect(401);
    await request(app.getHttpServer()).post('/api/v1/auth/login').send({ username: user.username, password: 'replacement-password-123' }).expect(201);
  });

  it('prevents concurrent admins from deleting one another using stale authorization', async () => {
    const first = await account('ACTIVE', true), second = await account('ACTIVE', true);
    const firstToken = await jwt.signAsync({ sub: first.id }), secondToken = await jwt.signAsync({ sub: second.id });
    const results = await Promise.all([
      request(app.getHttpServer()).delete(`/api/v1/users/${second.id}/permanent`).set('Authorization', `Bearer ${firstToken}`).send({ confirmation: second.username }),
      request(app.getHttpServer()).delete(`/api/v1/users/${first.id}/permanent`).set('Authorization', `Bearer ${secondToken}`).send({ confirmation: first.username }),
    ]);
    expect(results.filter((result) => result.status === 200)).toHaveLength(1);
    // The loser can fail either the bearer guard or the transactional actor recheck.
    expect([401, 403]).toContain(results.find((result) => result.status !== 200)?.status);
    expect(await prisma.user.count({ where: { id: { in: [first.id, second.id] } } })).toBe(1);
  });

  it('protects self deletion, the final active admin, and the ADMIN role', async () => {
    await prisma.user.updateMany({ where: { id: { not: admin.id } }, data: { active: false, status: 'DISABLED' } });
    await request(app.getHttpServer()).delete(`/api/v1/users/${admin.id}/permanent`).set(bearer()).send({ confirmation: admin.username }).expect(409);
    await request(app.getHttpServer()).delete(`/api/v1/users/${admin.id}`).set(bearer()).expect(409);
    await request(app.getHttpServer()).patch(`/api/v1/users/${admin.id}`).set(bearer()).send({ roleId: operatorRoleId }).expect(409);
    await request(app.getHttpServer()).delete(`/api/v1/roles/${roleId}`).set(bearer()).expect(409);
    await request(app.getHttpServer()).patch(`/api/v1/roles/${roleId}`).set(bearer()).send({ code: 'RENAMED' }).expect(409);
    expect(await prisma.user.findUnique({ where: { id: admin.id } })).toMatchObject({ active: true, status: 'ACTIVE', tokenVersion: 0 });
  });
});

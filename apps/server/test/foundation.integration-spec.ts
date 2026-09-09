import { INestApplication } from '@nestjs/common';
import { Test } from '@nestjs/testing';
import * as argon2 from 'argon2';
import request = require('supertest');
import { AppModule } from '../src/app.module';
import { configureApp } from '../src/configure-app';
import { PrismaService } from '../src/prisma/prisma.service';

describe('VPS foundation (PostgreSQL integration)', () => {
  let app: INestApplication;
  let prisma: PrismaService;
  let token: string;

  beforeAll(async () => {
    const moduleRef = await Test.createTestingModule({ imports: [AppModule] }).compile();
    app = moduleRef.createNestApplication();
    configureApp(app);
    await app.init();
    prisma = moduleRef.get(PrismaService);

    await prisma.userSiteRole.deleteMany();
    await prisma.device.deleteMany();
    await prisma.item.deleteMany();
    await prisma.user.deleteMany();
    await prisma.role.deleteMany();
    await prisma.site.deleteMany();

    const role = await prisma.role.create({
      data: { code: 'ADMIN', name: 'Administrator', permissions: ['*'] },
    });
    const user = await prisma.user.create({
      data: {
        username: 'integration-admin',
        displayName: 'Integration Admin',
        passwordHash: await argon2.hash('integration-password', {
          memoryCost: 4096,
          timeCost: 1,
        }),
      },
    });
    await prisma.userSiteRole.create({ data: { userId: user.id, roleId: role.id } });
  }, 120_000);

  afterAll(async () => {
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
      username: 'INTEGRATION-ADMIN',
      password: 'integration-password',
    });
    expect(response.status).toBe(201);
    expect(response.body.access_token).toEqual(expect.any(String));
    token = response.body.access_token as string;
  });

  it('creates the site, item, and matching-profile device foundations', async () => {
    const siteResponse = await request(app.getHttpServer())
      .post('/api/v1/sites')
      .set('Authorization', `Bearer ${token}`)
      .send({ code: 'branch-a', name: 'فرع أ', type: 'BRANCH_TYPE_1' })
      .expect(201);

    await request(app.getHttpServer())
      .post('/api/v1/items')
      .set('Authorization', `Bearer ${token}`)
      .send({
        sku: 'cake-001',
        nameAr: 'كيكة',
        unit: 'piece',
        quantityScale: 1,
        kind: 'PRODUCT',
      })
      .expect(201);

    await request(app.getHttpServer())
      .post('/api/v1/devices')
      .set('Authorization', `Bearer ${token}`)
      .send({ siteId: siteResponse.body.id, profile: 'BRANCH_TYPE_1' })
      .expect(201);

    const mismatch = await request(app.getHttpServer())
      .post('/api/v1/devices')
      .set('Authorization', `Bearer ${token}`)
      .send({ siteId: siteResponse.body.id, profile: 'KITCHEN' });
    expect(mismatch.status).toBe(422);
    expect(mismatch.body.code).toBe('BUSINESS_RULE_VIOLATION');
  });
});

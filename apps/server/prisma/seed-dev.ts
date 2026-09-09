import { DeviceProfile, EnrollmentStatus, ItemKind, PrismaClient, SiteType } from '@prisma/client';
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
    update: {},
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
      update: { name: demoRole.name, permissions: demoRole.permissions },
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
    { sku: 'CAKE-CHOC', nameAr: 'تورتة شوكولاتة', unit: 'قطعة', quantityScale: 1, kind: ItemKind.PRODUCT },
    { sku: 'GATEAUX-001', nameAr: 'جاتوه شوكولاتة', unit: 'قطعة', quantityScale: 1, kind: ItemKind.PRODUCT },
    { sku: 'ING-EGGS', nameAr: 'بيض', unit: 'بيضة', quantityScale: 1, kind: ItemKind.INGREDIENT },
    { sku: 'ING-FLOUR', nameAr: 'دقيق', unit: 'كجم', quantityScale: 1000, kind: ItemKind.INGREDIENT },
    { sku: 'ING-SUGAR', nameAr: 'سكر', unit: 'كجم', quantityScale: 1000, kind: ItemKind.INGREDIENT },
  ];
  for (const input of itemInputs) {
    await prisma.item.upsert({
      where: { sku: input.sku },
      update: { ...input, active: true },
      create: input,
    });
  }

  const profileForType: Record<SiteType, DeviceProfile> = {
    BRANCH_TYPE_1: DeviceProfile.BRANCH_TYPE_1,
    BRANCH_TYPE_2: DeviceProfile.BRANCH_TYPE_2,
    KITCHEN: DeviceProfile.KITCHEN,
  };
  for (const site of sites) {
    const keyThumbprint = `development-only-${site.code.toLowerCase()}`;
    await prisma.device.upsert({
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
  }
}

main()
  .finally(async () => prisma.$disconnect())
  .catch((error: unknown) => {
    console.error(error);
    process.exitCode = 1;
  });

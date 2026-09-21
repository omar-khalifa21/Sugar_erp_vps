import { PrismaClient } from '@prisma/client';
import * as argon2 from 'argon2';

const prisma = new PrismaClient();

async function main(): Promise<void> {
  const username = process.env.BOOTSTRAP_ADMIN_USERNAME?.trim().toLowerCase();
  const password = process.env.BOOTSTRAP_ADMIN_PASSWORD;
  if (!username) throw new Error('BOOTSTRAP_ADMIN_USERNAME is required');
  if (!password || password.length < 12) {
    throw new Error('BOOTSTRAP_ADMIN_PASSWORD must contain at least 12 characters');
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
      displayName: 'System Administrator',
      passwordHash,
      active: true,
    },
    create: {
      username,
      displayName: 'System Administrator',
      passwordHash,
    },
  });

  const assignment = await prisma.userSiteRole.findFirst({
    where: { userId: user.id, roleId: role.id, siteId: null },
  });
  if (!assignment) {
    await prisma.userSiteRole.create({ data: { userId: user.id, roleId: role.id } });
  }
}

main()
  .catch((error: unknown) => {
    console.error(error);
    process.exitCode = 1;
  })
  .finally(async () => {
    await prisma.$disconnect();
  });

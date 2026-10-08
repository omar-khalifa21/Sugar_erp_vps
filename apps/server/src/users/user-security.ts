import { ConflictException, ForbiddenException } from '@nestjs/common';
import { Prisma } from '@prisma/client';

export async function lockUserAdministration(tx: Prisma.TransactionClient, actorId: string) {
  // All user/role removals share this lock, including concurrent admins deleting each other.
  await tx.$queryRaw(Prisma.sql`SELECT "id" FROM "roles" WHERE "code" = 'ADMIN' FOR UPDATE`);
  const actor = await tx.user.findFirst({ where: {
    id: actorId, active: true, status: 'ACTIVE',
    siteRoles: { some: { role: { code: 'ADMIN', active: true } } },
  } });
  if (!actor) throw new ForbiddenException('This operation requires an active administrator');
  return actor;
}

export async function requireRemainingAdmin(tx: Prisma.TransactionClient) {
  const count = await tx.user.count({ where: { active: true, status: 'ACTIVE',
    siteRoles: { some: { role: { code: 'ADMIN', active: true } } },
  } });
  if (!count) throw new ConflictException({ code: 'LAST_ADMIN', message: 'The last active administrator cannot be removed' });
}

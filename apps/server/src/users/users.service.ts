import { BadRequestException, ConflictException, Injectable, NotFoundException } from '@nestjs/common';
import { Prisma } from '@prisma/client';
import * as argon2 from 'argon2';
import { PrismaService } from '../prisma/prisma.service';
import { CreateUserDto } from './create-user.dto';
import { UpdateUserDto } from './update-user.dto';
import { ApproveUserDto } from './approve-user.dto';
import { lockUserAdministration, requireRemainingAdmin } from './user-security';

const safeUserSelect = {
  id: true,
  username: true,
  displayName: true,
  active: true,
  status: true,
  createdAt: true,
  updatedAt: true,
  siteRoles: { include: { role: true, site: true } },
} satisfies Prisma.UserSelect;

export type SafeUser = Prisma.UserGetPayload<{ select: typeof safeUserSelect }>;

@Injectable()
export class UsersService {
  constructor(private readonly prisma: PrismaService) {}

  list(): Promise<SafeUser[]> {
    return this.prisma.user.findMany({ select: safeUserSelect, orderBy: { username: 'asc' } });
  }

  async create(input: CreateUserDto): Promise<SafeUser> {
    return this.prisma.user.create({
      data: {
        username: input.username.trim().toLowerCase(),
        displayName: input.displayName.trim(),
        passwordHash: await argon2.hash(input.password),
        siteRoles: {
          create: { roleId: input.roleId, ...(input.siteId ? { siteId: input.siteId } : {}) },
        },
        status: 'ACTIVE',
      },
      select: safeUserSelect,
    });
  }

  async update(id: string, input: UpdateUserDto, actorId: string): Promise<SafeUser> {
    const passwordHash = input.password ? await argon2.hash(input.password) : undefined;
    return this.prisma.$transaction(async (transaction) => {
      const actor = await lockUserAdministration(transaction, actorId);
      const original = await transaction.user.findUnique({ where: { id } });
      if (!original) throw new NotFoundException('User not found');
      if (input.roleId) await this.validateAssignment(transaction, input);
      await transaction.user.update({
        where: { id },
        data: {
          ...(input.username !== undefined ? { username: input.username.trim().toLowerCase() } : {}),
          ...(input.displayName !== undefined ? { displayName: input.displayName.trim() } : {}),
          ...(passwordHash ? { passwordHash } : {}),
          ...(input.roleId ? { tokenVersion: { increment: 1 } } : {}),
          ...(input.active !== undefined ? { active: input.active, status: input.active ? 'ACTIVE' : 'DISABLED' } : {}),
        },
      });
      if (input.roleId) {
        await transaction.userSiteRole.deleteMany({ where: { userId: id } });
        await transaction.userSiteRole.create({
          data: { userId: id, roleId: input.roleId, ...(input.siteId ? { siteId: input.siteId } : {}) },
        });
        if (input.active !== false) {
          await transaction.user.update({ where: { id }, data: { status: 'ACTIVE', active: true } });
        }
      }
      await requireRemainingAdmin(transaction);
      if (original.status === 'PENDING_PERMISSION' && (input.roleId || input.active === true) && input.active !== false) {
        await transaction.userAuditEvent.create({ data: { actorId, actorUsername: actor.username,
          targetId: id, targetUsername: original.username, action: 'USER_APPROVED' } });
      }
      return transaction.user.findUniqueOrThrow({ where: { id }, select: safeUserSelect });
    });
  }

  archive(id: string, actorId: string): Promise<SafeUser> {
    return this.update(id, { active: false }, actorId);
  }

  async approve(id: string, input: ApproveUserDto, actorId: string): Promise<SafeUser> {
    return this.prisma.$transaction(async (tx) => {
      const actor = await lockUserAdministration(tx, actorId);
      const user = await tx.user.findUnique({ where: { id } });
      if (!user) throw new NotFoundException('User not found');
      if (user.status !== 'PENDING_PERMISSION') throw new ConflictException('Only pending users can be approved');
      await this.validateAssignment(tx, input);
      await tx.userSiteRole.deleteMany({ where: { userId: id } });
      await tx.userSiteRole.create({ data: { userId: id, roleId: input.roleId, siteId: input.siteId } });
      const result = await tx.user.update({ where: { id }, data: { active: true, status: 'ACTIVE' }, select: safeUserSelect });
      await tx.userAuditEvent.create({ data: { actorId, actorUsername: actor.username,
        targetId: id, targetUsername: user.username, action: 'USER_APPROVED' } });
      return result;
    });
  }

  async deletePermanently(id: string, confirmation: string, actorId: string, pendingOnly = false) {
    return this.prisma.$transaction(async (tx) => {
      const actor = await lockUserAdministration(tx, actorId);
      const user = await tx.user.findUnique({ where: { id } });
      if (!user) throw new NotFoundException('User not found');
      if (id === actorId) throw new ConflictException({ code: 'SELF_DELETE', message: 'You cannot permanently delete your own account' });
      if (confirmation !== user.username) throw new BadRequestException({ code: 'CONFIRMATION_MISMATCH', message: 'Type the exact username to confirm deletion' });
      if (pendingOnly && user.status !== 'PENDING_PERMISSION') throw new ConflictException('Only pending users can be rejected');
      await tx.userAuditEvent.create({ data: { actorId, actorUsername: actor.username,
        targetId: id, targetUsername: user.username, action: pendingOnly ? 'USER_REJECTED' : 'USER_DELETED' } });
      await tx.userSiteRole.deleteMany({ where: { userId: id } });
      // These are credentials issued by this account, not immutable business facts.
      await tx.enrollmentToken.deleteMany({ where: { createdByUserId: id } });
      await tx.user.delete({ where: { id } });
      await requireRemainingAdmin(tx);
      // Retain attribution only when a posted business record needs it.
      await tx.userIdentity.deleteMany({ where: { id, stockAdjustments: { none: {} },
        adminDecisions: { none: {} }, inventoryTransactions: { none: {} } } });
      return { id, deleted: true };
    });
  }

  private async validateAssignment(tx: Prisma.TransactionClient, input: { roleId?: string; siteId?: string }) {
    if (!await tx.role.findFirst({ where: { id: input.roleId, active: true } })) throw new BadRequestException('Choose an active role');
    if (input.siteId && !await tx.site.findFirst({ where: { id: input.siteId, active: true } })) throw new BadRequestException('Choose an active site');
  }
}

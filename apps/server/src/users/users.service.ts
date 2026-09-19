import { Injectable } from '@nestjs/common';
import { Prisma } from '@prisma/client';
import * as argon2 from 'argon2';
import { PrismaService } from '../prisma/prisma.service';
import { CreateUserDto } from './create-user.dto';
import { UpdateUserDto } from './update-user.dto';

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

  async update(id: string, input: UpdateUserDto): Promise<SafeUser> {
    const passwordHash = input.password ? await argon2.hash(input.password) : undefined;
    return this.prisma.$transaction(async (transaction) => {
      await transaction.user.update({
        where: { id },
        data: {
          ...(input.username !== undefined ? { username: input.username.trim().toLowerCase() } : {}),
          ...(input.displayName !== undefined ? { displayName: input.displayName.trim() } : {}),
          ...(passwordHash ? { passwordHash } : {}),
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
      return transaction.user.findUniqueOrThrow({ where: { id }, select: safeUserSelect });
    });
  }

  archive(id: string): Promise<SafeUser> {
    return this.prisma.user.update({ where: { id }, data: { active: false, status: 'DISABLED' }, select: safeUserSelect });
  }
}

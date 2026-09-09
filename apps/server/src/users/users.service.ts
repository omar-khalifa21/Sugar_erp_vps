import { Injectable } from '@nestjs/common';
import { Prisma } from '@prisma/client';
import * as argon2 from 'argon2';
import { PrismaService } from '../prisma/prisma.service';
import { CreateUserDto } from './create-user.dto';

const safeUserSelect = {
  id: true,
  username: true,
  displayName: true,
  active: true,
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
      },
      select: safeUserSelect,
    });
  }
}

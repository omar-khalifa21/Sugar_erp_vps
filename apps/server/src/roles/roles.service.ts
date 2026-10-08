import { ConflictException, Injectable, NotFoundException } from '@nestjs/common';
import { Role } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { CreateRoleDto } from './create-role.dto';
import { UpdateRoleDto } from './update-role.dto';
import { lockUserAdministration, requireRemainingAdmin } from '../users/user-security';

@Injectable()
export class RolesService {
  constructor(private readonly prisma: PrismaService) {}

  list(): Promise<Role[]> {
    return this.prisma.role.findMany({ orderBy: { code: 'asc' } });
  }

  create(input: CreateRoleDto): Promise<Role> {
    return this.prisma.role.create({
      data: {
        code: input.code.trim().toUpperCase(),
        name: input.name.trim(),
        permissions: input.permissions,
      },
    });
  }

  update(id: string, input: UpdateRoleDto, actorId: string): Promise<Role> {
    return this.prisma.$transaction(async (tx) => {
      await lockUserAdministration(tx, actorId);
      const role = await tx.role.findUnique({ where: { id } });
      if (!role) throw new NotFoundException('Role not found');
      if (role.code === 'ADMIN' && (input.active === false || (input.code !== undefined && input.code.trim().toUpperCase() !== 'ADMIN'))) {
        throw new ConflictException({ code: 'ADMIN_ROLE_REQUIRED', message: 'The administrator role cannot be renamed or disabled' });
      }
      const updated = await tx.role.update({
      where: { id },
      data: {
        ...(input.code !== undefined ? { code: input.code.trim().toUpperCase() } : {}),
        ...(input.name !== undefined ? { name: input.name.trim() } : {}),
        ...(input.permissions !== undefined ? { permissions: input.permissions } : {}),
        ...(input.active !== undefined ? { active: input.active } : {}),
      },
      });
      await requireRemainingAdmin(tx);
      return updated;
    });
  }

  archive(id: string, actorId: string): Promise<Role> {
    return this.update(id, { active: false }, actorId);
  }
}

import { Injectable } from '@nestjs/common';
import { Role } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { CreateRoleDto } from './create-role.dto';
import { UpdateRoleDto } from './update-role.dto';

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

  update(id: string, input: UpdateRoleDto): Promise<Role> {
    return this.prisma.role.update({
      where: { id },
      data: {
        ...(input.code !== undefined ? { code: input.code.trim().toUpperCase() } : {}),
        ...(input.name !== undefined ? { name: input.name.trim() } : {}),
        ...(input.permissions !== undefined ? { permissions: input.permissions } : {}),
        ...(input.active !== undefined ? { active: input.active } : {}),
      },
    });
  }

  archive(id: string): Promise<Role> {
    return this.prisma.role.update({ where: { id }, data: { active: false } });
  }
}

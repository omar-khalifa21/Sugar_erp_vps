import { Injectable } from '@nestjs/common';
import { Role } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { CreateRoleDto } from './create-role.dto';

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
}

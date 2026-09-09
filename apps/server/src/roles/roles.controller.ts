import { Body, Controller, Get, Post } from '@nestjs/common';
import { Role } from '@prisma/client';
import { CreateRoleDto } from './create-role.dto';
import { RolesService } from './roles.service';

@Controller('roles')
export class RolesController {
  constructor(private readonly roles: RolesService) {}

  @Get()
  list(): Promise<Role[]> {
    return this.roles.list();
  }

  @Post()
  create(@Body() input: CreateRoleDto): Promise<Role> {
    return this.roles.create(input);
  }
}

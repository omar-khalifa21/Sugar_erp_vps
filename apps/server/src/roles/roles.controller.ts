import { Body, Controller, Delete, Get, Param, ParseUUIDPipe, Patch, Post, Req } from '@nestjs/common';
import { Role } from '@prisma/client';
import { CreateRoleDto } from './create-role.dto';
import { RolesService } from './roles.service';
import { Roles } from '../common/roles.decorator';
import { UpdateRoleDto } from './update-role.dto';

@Roles('ADMIN')
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

  @Patch(':id')
  update(@Param('id', ParseUUIDPipe) id: string, @Body() input: UpdateRoleDto, @Req() request: { user: { id: string } }): Promise<Role> {
    return this.roles.update(id, input, request.user.id);
  }

  @Delete(':id')
  archive(@Param('id', ParseUUIDPipe) id: string, @Req() request: { user: { id: string } }): Promise<Role> {
    return this.roles.archive(id, request.user.id);
  }
}

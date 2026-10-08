import { Body, Controller, Delete, Get, Param, ParseUUIDPipe, Patch, Post, Req } from '@nestjs/common';
import { CreateUserDto } from './create-user.dto';
import { SafeUser, UsersService } from './users.service';
import { Roles } from '../common/roles.decorator';
import { UpdateUserDto } from './update-user.dto';
import { ConfirmUserDto } from './confirm-user.dto';
import { ApproveUserDto } from './approve-user.dto';

@Roles('ADMIN')
@Controller('users')
export class UsersController {
  constructor(private readonly users: UsersService) {}

  @Get()
  list(): Promise<SafeUser[]> {
    return this.users.list();
  }

  @Post()
  create(@Body() input: CreateUserDto): Promise<SafeUser> {
    return this.users.create(input);
  }

  @Patch(':id')
  update(@Param('id', ParseUUIDPipe) id: string, @Body() input: UpdateUserDto, @Req() request: { user: { id: string } }): Promise<SafeUser> {
    return this.users.update(id, input, request.user.id);
  }

  @Delete(':id')
  archive(@Param('id', ParseUUIDPipe) id: string, @Req() request: { user: { id: string } }): Promise<SafeUser> {
    return this.users.archive(id, request.user.id);
  }

  @Post(':id/approve')
  approve(@Param('id', ParseUUIDPipe) id: string, @Body() input: ApproveUserDto, @Req() request: { user: { id: string } }) {
    return this.users.approve(id, input, request.user.id);
  }

  @Post(':id/reject')
  reject(@Param('id', ParseUUIDPipe) id: string, @Body() input: ConfirmUserDto, @Req() request: { user: { id: string } }) {
    return this.users.deletePermanently(id, input.confirmation, request.user.id, true);
  }

  @Delete(':id/permanent')
  deletePermanently(@Param('id', ParseUUIDPipe) id: string, @Body() input: ConfirmUserDto, @Req() request: { user: { id: string } }) {
    return this.users.deletePermanently(id, input.confirmation, request.user.id);
  }
}

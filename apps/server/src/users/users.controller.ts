import { Body, Controller, Delete, Get, Param, ParseUUIDPipe, Patch, Post } from '@nestjs/common';
import { CreateUserDto } from './create-user.dto';
import { SafeUser, UsersService } from './users.service';
import { Roles } from '../common/roles.decorator';
import { UpdateUserDto } from './update-user.dto';

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
  update(@Param('id', ParseUUIDPipe) id: string, @Body() input: UpdateUserDto): Promise<SafeUser> {
    return this.users.update(id, input);
  }

  @Delete(':id')
  archive(@Param('id', ParseUUIDPipe) id: string): Promise<SafeUser> {
    return this.users.archive(id);
  }
}

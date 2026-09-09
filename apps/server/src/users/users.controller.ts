import { Body, Controller, Get, Post } from '@nestjs/common';
import { CreateUserDto } from './create-user.dto';
import { SafeUser, UsersService } from './users.service';

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
}

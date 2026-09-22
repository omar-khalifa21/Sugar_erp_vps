import { Body, Controller, Delete, Get, Param, ParseUUIDPipe, Patch, Post } from '@nestjs/common';
import { Item } from '@prisma/client';
import { CreateItemDto } from './create-item.dto';
import { ItemsService } from './items.service';
import { Roles } from '../common/roles.decorator';
import { UpdateItemDto } from './update-item.dto';

@Roles('ADMIN')
@Controller('items')
export class ItemsController {
  constructor(private readonly items: ItemsService) {}

  @Get()
  list(): ReturnType<ItemsService['list']> {
    return this.items.list();
  }

  @Post()
  create(@Body() input: CreateItemDto): Promise<Item> {
    return this.items.create(input);
  }

  @Patch(':id')
  update(@Param('id', ParseUUIDPipe) id: string, @Body() input: UpdateItemDto): Promise<Item> {
    return this.items.update(id, input);
  }

  @Delete(':id')
  archive(@Param('id', ParseUUIDPipe) id: string): Promise<Item> {
    return this.items.archive(id);
  }
}

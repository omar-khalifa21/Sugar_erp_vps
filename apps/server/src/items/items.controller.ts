import { Body, Controller, Get, Post } from '@nestjs/common';
import { Item } from '@prisma/client';
import { CreateItemDto } from './create-item.dto';
import { ItemsService } from './items.service';

@Controller('items')
export class ItemsController {
  constructor(private readonly items: ItemsService) {}

  @Get()
  list(): Promise<Item[]> {
    return this.items.list();
  }

  @Post()
  create(@Body() input: CreateItemDto): Promise<Item> {
    return this.items.create(input);
  }
}

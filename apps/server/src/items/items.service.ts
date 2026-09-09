import { Injectable } from '@nestjs/common';
import { Item } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { CreateItemDto } from './create-item.dto';

@Injectable()
export class ItemsService {
  constructor(private readonly prisma: PrismaService) {}

  list(): Promise<Item[]> {
    return this.prisma.item.findMany({ orderBy: { sku: 'asc' } });
  }

  create(input: CreateItemDto): Promise<Item> {
    return this.prisma.item.create({
      data: {
        sku: input.sku.trim().toUpperCase(),
        nameAr: input.nameAr.trim(),
        unit: input.unit.trim(),
        quantityScale: input.quantityScale,
        kind: input.kind,
      },
    });
  }
}

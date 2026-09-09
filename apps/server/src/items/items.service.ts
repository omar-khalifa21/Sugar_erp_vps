import { Injectable } from '@nestjs/common';
import { Item } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { CreateItemDto } from './create-item.dto';
import { UpdateItemDto } from './update-item.dto';

@Injectable()
export class ItemsService {
  constructor(private readonly prisma: PrismaService) {}

  list(): Promise<Item[]> {
    return this.prisma.item.findMany({ orderBy: { sku: 'asc' } });
  }

  create(input: CreateItemDto): Promise<Item> {
    return this.prisma.$transaction(async (transaction) => {
      const item = await transaction.item.create({
        data: {
          sku: input.sku.trim().toUpperCase(),
          nameAr: input.nameAr.trim(),
          unit: input.unit.trim(),
          quantityScale: input.quantityScale,
          retailPriceMinor: input.retailPriceMinor ?? 0,
          kind: input.kind,
        },
      });
      await transaction.retailPriceRevision.create({
        data: { itemId: item.id, priceMinor: item.retailPriceMinor, version: item.version },
      });
      return item;
    });
  }

  update(id: string, input: UpdateItemDto): Promise<Item> {
    return this.prisma.$transaction(async (transaction) => {
      const before = await transaction.item.findUniqueOrThrow({ where: { id } });
      const item = await transaction.item.update({
        where: { id },
        data: {
          ...(input.sku !== undefined ? { sku: input.sku.trim().toUpperCase() } : {}),
          ...(input.nameAr !== undefined ? { nameAr: input.nameAr.trim() } : {}),
          ...(input.unit !== undefined ? { unit: input.unit.trim() } : {}),
          ...(input.quantityScale !== undefined ? { quantityScale: input.quantityScale } : {}),
          ...(input.retailPriceMinor !== undefined ? { retailPriceMinor: input.retailPriceMinor } : {}),
          ...(input.kind !== undefined ? { kind: input.kind } : {}),
          ...(input.active !== undefined ? { active: input.active } : {}),
          version: { increment: 1 },
        },
      });
      if (input.retailPriceMinor !== undefined && input.retailPriceMinor !== before.retailPriceMinor) {
        await transaction.retailPriceRevision.create({
          data: { itemId: item.id, priceMinor: item.retailPriceMinor, version: item.version },
        });
      }
      return item;
    });
  }

  archive(id: string): Promise<Item> {
    return this.prisma.item.update({
      where: { id },
      data: { active: false, version: { increment: 1 } },
    });
  }
}

import { Injectable } from '@nestjs/common';
import { DeviceProfile, EnrollmentStatus, Item, Prisma } from '@prisma/client';
import { randomUUID } from 'node:crypto';
import { PrismaService } from '../prisma/prisma.service';
import { CreateItemDto } from './create-item.dto';
import { UpdateItemDto } from './update-item.dto';
import { computeEventHash } from '../sync/sync.service';

@Injectable()
export class ItemsService {
  constructor(private readonly prisma: PrismaService) {}

  list(): Promise<Item[]> {
    return this.prisma.item.findMany({ orderBy: [{ nameAr: 'asc' }, { createdAt: 'asc' }] });
  }

  create(input: CreateItemDto): Promise<Item> {
    return this.prisma.$transaction(async (transaction) => {
      const id = randomUUID();
      const item = await transaction.item.create({
        data: {
          id,
          sku: input.sku?.trim().toUpperCase() || `AUTO-${id}`,
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
      await this.publishCatalogItem(transaction, item, 'catalog.item_published');
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
      await this.publishCatalogItem(transaction, item, 'catalog.item.updated');
      return item;
    });
  }

  archive(id: string): Promise<Item> {
    return this.prisma.$transaction(async (transaction) => {
      const item = await transaction.item.update({
        where: { id },
        data: { active: false, version: { increment: 1 } },
      });
      await this.publishCatalogItem(transaction, item, 'catalog.item.updated');
      return item;
    });
  }

  private async publishCatalogItem(
    transaction: Prisma.TransactionClient,
    item: Item,
    eventType: 'catalog.item_published' | 'catalog.item.updated',
  ): Promise<void> {
    const sites = await transaction.site.findMany({ where: { active: true }, select: { id: true, type: true } });
    for (const site of sites) {
      let device = await transaction.device.findFirst({ where: { siteId: site.id, name: '__SERVER_SYNC__' } });
      if (!device) {
        device = await transaction.device.create({
          data: {
            siteId: site.id,
            profile: site.type as DeviceProfile,
            enrollmentStatus: EnrollmentStatus.ENROLLED,
            activeWriter: false,
            name: '__SERVER_SYNC__',
            appVersion: 'server',
            streamEpoch: 1,
            syncState: { create: { nextExpectedSequence: 1 } },
            syncCursor: { create: { lastServerPosition: 0 } },
          },
        });
      }
      await transaction.$queryRaw(Prisma.sql`SELECT device_id FROM device_sync_states WHERE device_id = ${device.id}::uuid FOR UPDATE`);
      const state = await transaction.deviceSyncState.findUniqueOrThrow({ where: { deviceId: device.id } });
      const occurredAt = new Date();
      const id = randomUUID();
      const payload = {
        item_id: item.id,
        sku: item.sku,
        name_ar: item.nameAr,
        unit: item.unit,
        quantity_scale: item.quantityScale,
        retail_price_minor: item.retailPriceMinor,
        kind: item.kind,
        active: item.active,
        version: item.version,
      };
      const event = {
        id,
        device_sequence: state.nextExpectedSequence,
        event_type: eventType,
        schema_version: 1,
        occurred_at: occurredAt.toISOString(),
        payload,
        dependencies: [] as string[],
      };
      await transaction.syncEvent.create({
        data: {
          id,
          deviceId: device.id,
          siteId: site.id,
          streamEpoch: 1,
          deviceSequence: state.nextExpectedSequence,
          eventType,
          schemaVersion: 1,
          occurredAt,
          payload,
          dependencies: [],
          contentHash: computeEventHash(event),
        },
      });
      await transaction.deviceSyncState.update({
        where: { deviceId: device.id },
        data: { nextExpectedSequence: { increment: 1 }, lastAcceptedAt: occurredAt },
      });
    }
  }
}

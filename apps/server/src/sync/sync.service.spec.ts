import { ConfigService } from '@nestjs/config';
import { DeviceProfile, SiteType } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { DeviceAuthService } from './device-auth.service';
import { SyncService } from './sync.service';
import { TransferProjectionService } from './transfer-projection.service';

describe('SyncService routing', () => {
  const siteId = '9cf1a57b-0e16-4e3b-b2f4-381ab3f5cff4';
  const deviceId = 'd86688f7-8206-48ee-854e-fdc3af266b17';
  const findMany = jest.fn().mockResolvedValue([]);
  const authenticate = jest.fn();
  const service = new SyncService(
    { syncEvent: { findMany } } as unknown as PrismaService,
    { authenticate } as unknown as DeviceAuthService,
    { getOrThrow: () => 'test-only-cursor-signing-key' } as unknown as ConfigService,
    { apply: jest.fn() } as unknown as TransferProjectionService,
  );

  beforeEach(() => {
    findMany.mockClear();
    authenticate.mockReset();
  });

  it('routes branch request submissions to a kitchen pull without exposing other event types', async () => {
    authenticate.mockResolvedValue({ id: deviceId, siteId, profile: DeviceProfile.KITCHEN });
    await service.pull(deviceId, 'test-secret', undefined, 100);
    expect(findMany).toHaveBeenCalledWith(
      expect.objectContaining({
        where: {
          OR: [
            { siteId, deviceId: { not: deviceId } },
            { eventType: { in: ['catalog.item_published', 'catalog.item.updated', 'catalog.item.deleted'] } },
            { eventType: { in: ['cafe_customer.created', 'cafe_customer.updated', 'cafe_customer.archived', 'cafe_customer.price_list_updated'] } },
            {
              eventType: 'kitchen_request.submitted',
              site: { type: { in: [SiteType.BRANCH_TYPE_1, SiteType.BRANCH_TYPE_2] } },
            },
            {
              eventType: { in: ['incoming_receipt.accepted', 'incoming_receipt.disputed'] },
              site: { type: { in: [SiteType.BRANCH_TYPE_1, SiteType.BRANCH_TYPE_2] } },
            },
            {
              eventType: 'kitchen_return.dispatched',
              site: { type: { in: [SiteType.BRANCH_TYPE_1, SiteType.BRANCH_TYPE_2] } },
            },
          ],
          serverPosition: { gt: 0n },
        },
      }),
    );
  });

  it('routes only addressed kitchen dispatches to a branch pull', async () => {
    authenticate.mockResolvedValue({ id: deviceId, siteId, profile: DeviceProfile.BRANCH_TYPE_1 });
    await service.pull(deviceId, 'test-secret', undefined, 100);
    expect(findMany).toHaveBeenCalledWith(
      expect.objectContaining({
        where: {
          OR: [
            {
              siteId,
              deviceId: { not: deviceId },
              NOT: { eventType: { in: ['catalog.item_published', 'catalog.item.updated', 'catalog.item.deleted'] } },
            },
            {
              eventType: { in: ['catalog.item_published', 'catalog.item.updated', 'catalog.item.deleted'] },
              payload: { path: ['kind'], equals: 'PRODUCT' },
            },
            { eventType: { in: ['cafe_customer.created', 'cafe_customer.updated', 'cafe_customer.archived', 'cafe_customer.price_list_updated'] } },
            {
              eventType: 'shipment.dispatched',
              site: { type: SiteType.KITCHEN },
              payload: { path: ['destination_site_id'], equals: siteId },
            },
            {
              eventType: 'kitchen_request.received',
              site: { type: SiteType.KITCHEN },
              payload: { path: ['destination_site_id'], equals: siteId },
            },
          ],
          serverPosition: { gt: 0n },
        },
      }),
    );
  });

  it('returns the exact hash-critical timestamp originally uploaded by the device', async () => {
    const exactOccurredAt = '2026-09-22T12:32:03.3157266+00:00';
    authenticate.mockResolvedValue({ id: deviceId, siteId, profile: DeviceProfile.BRANCH_TYPE_1 });
    findMany.mockResolvedValueOnce([{
      id: 'e68d9482-c0f0-4f92-aecd-27d98089605b',
      deviceId: 'c54a19ca-326c-4589-b14b-972e150bf9bc',
      siteId: '9e4f6ddb-dc06-4963-988b-36a035a0f0fb',
      streamEpoch: 1,
      deviceSequence: 1,
      eventType: 'kitchen_request.received',
      schemaVersion: 1,
      occurredAt: new Date('2026-09-22T12:32:03.315Z'),
      occurredAtRaw: exactOccurredAt,
      receivedAt: new Date('2026-09-22T12:32:04.000Z'),
      payload: { request_id: '907cc0cd-6ef5-4f6c-b2b3-dd3c4e772857', destination_site_id: siteId },
      dependencies: [],
      contentHash: 'a'.repeat(64),
      serverPosition: 75n,
    }]);

    const result = await service.pull(deviceId, 'test-secret', undefined, 100);

    expect(result.events).toHaveLength(1);
    expect(result.events[0].occurred_at).toBe(exactOccurredAt);
  });

  it('filters ingredients out of a branch bootstrap catalog', async () => {
    const itemFindMany = jest.fn().mockResolvedValue([]);
    const bootstrapService = new SyncService(
      {
        site: { findUniqueOrThrow: jest.fn().mockResolvedValue({ id: siteId, name: 'Branch', type: SiteType.BRANCH_TYPE_1, timezone: 'Africa/Cairo' }) },
        item: { findMany: itemFindMany },
        cafeCustomer: { findMany: jest.fn().mockResolvedValue([]) },
        stockBalance: { findMany: jest.fn().mockResolvedValue([]) },
      } as unknown as PrismaService,
      { authenticate: jest.fn().mockResolvedValue({ id: deviceId, siteId, profile: DeviceProfile.BRANCH_TYPE_1 }) } as unknown as DeviceAuthService,
      { getOrThrow: () => 'test-only-cursor-signing-key' } as unknown as ConfigService,
      { apply: jest.fn() } as unknown as TransferProjectionService,
    );

    await bootstrapService.bootstrap(deviceId, 'test-secret');

    expect(itemFindMany).toHaveBeenCalledWith(expect.objectContaining({ where: { active: true, kind: 'PRODUCT' } }));
  });
});

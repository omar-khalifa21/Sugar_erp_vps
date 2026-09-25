import { DeviceProfile, ItemKind, SiteType } from '@prisma/client';
import { ItemsService } from './items.service';

describe('ItemsService', () => {
  it('creates an item without requiring a user-facing code', async () => {
    const create = jest.fn(({ data }) => Promise.resolve({ ...data, version: 1 }));
    const revision = jest.fn().mockResolvedValue({});
    const systemDeviceId = '00000000-0000-0000-0000-000000000010';
    const syncEventCreate = jest.fn().mockResolvedValue({});
    const transaction = {
      item: { create },
      retailPriceRevision: { create: revision },
      site: { findMany: jest.fn()
        .mockResolvedValueOnce([{ id: '00000000-0000-0000-0000-000000000020' }])
        .mockResolvedValueOnce([{ id: '00000000-0000-0000-0000-000000000020', type: SiteType.BRANCH_TYPE_1 }]) },
      siteRetailPrice: {
        createMany: jest.fn().mockResolvedValue({ count: 1 }),
        findUnique: jest.fn().mockResolvedValue({ priceMinor: 2500, version: 1 }),
      },
      siteRetailPriceRevision: { createMany: jest.fn().mockResolvedValue({ count: 1 }) },
      device: { findFirst: jest.fn().mockResolvedValue({ id: systemDeviceId, profile: DeviceProfile.BRANCH_TYPE_1 }) },
      deviceSyncState: {
        findUniqueOrThrow: jest.fn().mockResolvedValue({ deviceId: systemDeviceId, nextExpectedSequence: 4 }),
        update: jest.fn().mockResolvedValue({}),
      },
      syncEvent: { create: syncEventCreate },
      $queryRaw: jest.fn().mockResolvedValue([]),
    };
    const prisma = {
      $transaction: jest.fn((operation: (client: typeof transaction) => unknown) => operation(transaction)),
    };
    const service = new ItemsService(prisma as never);

    await service.create({
      nameAr: 'كيكة شوكولاتة',
      unit: 'قطعة',
      quantityScale: 1,
      retailPriceMinor: 2500,
      kind: ItemKind.PRODUCT,
    });

    // Jest asymmetric matchers intentionally expose `any` in their public typings.
    /* eslint-disable @typescript-eslint/no-unsafe-assignment */
    expect(create).toHaveBeenCalledWith({
      data: expect.objectContaining({
        nameAr: 'كيكة شوكولاتة',
        sku: expect.stringMatching(/^AUTO-[0-9a-f-]{36}$/),
      }),
    });
    expect(revision).toHaveBeenCalledTimes(1);
    expect(syncEventCreate).toHaveBeenCalledWith({
      data: expect.objectContaining({
        deviceId: systemDeviceId,
        deviceSequence: 4,
        eventType: 'catalog.item_published',
        payload: expect.objectContaining({ name_ar: 'كيكة شوكولاتة', retail_price_minor: 2500,
          price_site_id: '00000000-0000-0000-0000-000000000020' }),
        contentHash: expect.stringMatching(/^[a-f0-9]{64}$/),
      }),
    });
    /* eslint-enable @typescript-eslint/no-unsafe-assignment */
  });

  it('publishes ingredients to Kitchen only and creates no branch retail prices', async () => {
    const kitchenSiteId = '00000000-0000-0000-0000-000000000030';
    const systemDeviceId = '00000000-0000-0000-0000-000000000031';
    const createMany = jest.fn();
    const syncEventCreate = jest.fn().mockResolvedValue({});
    const siteFindMany = jest.fn().mockResolvedValue([{ id: kitchenSiteId, type: SiteType.KITCHEN }]);
    const transaction = {
      item: { create: jest.fn(({ data }) => Promise.resolve({ ...data, version: 1 })) },
      retailPriceRevision: { create: jest.fn().mockResolvedValue({}) },
      site: { findMany: siteFindMany },
      siteRetailPrice: { createMany, findUnique: jest.fn() },
      siteRetailPriceRevision: { createMany: jest.fn() },
      device: { findFirst: jest.fn().mockResolvedValue({ id: systemDeviceId, profile: DeviceProfile.KITCHEN }) },
      deviceSyncState: {
        findUniqueOrThrow: jest.fn().mockResolvedValue({ deviceId: systemDeviceId, nextExpectedSequence: 1 }),
        update: jest.fn().mockResolvedValue({}),
      },
      syncEvent: { create: syncEventCreate },
      $queryRaw: jest.fn().mockResolvedValue([]),
    };
    const prisma = { $transaction: jest.fn((operation: (client: typeof transaction) => unknown) => operation(transaction)) };

    await new ItemsService(prisma as never).create({
      nameAr: 'دقيق', unit: 'g', quantityScale: 1, retailPriceMinor: 0, kind: ItemKind.INGREDIENT,
    });

    expect(createMany).not.toHaveBeenCalled();
    expect(siteFindMany).toHaveBeenCalledTimes(1);
    expect(siteFindMany).toHaveBeenCalledWith({
      where: { active: true, type: 'KITCHEN' }, select: { id: true, type: true },
    });
    expect(syncEventCreate).toHaveBeenCalledWith({
      data: expect.objectContaining({ siteId: kitchenSiteId, payload: expect.objectContaining({ kind: ItemKind.INGREDIENT }) }),
    });
  });
});

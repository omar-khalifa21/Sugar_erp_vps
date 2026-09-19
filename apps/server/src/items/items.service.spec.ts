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
      site: { findMany: jest.fn().mockResolvedValue([{ id: '00000000-0000-0000-0000-000000000020', type: SiteType.BRANCH_TYPE_1 }]) },
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
        payload: expect.objectContaining({ name_ar: 'كيكة شوكولاتة', retail_price_minor: 2500 }),
        contentHash: expect.stringMatching(/^[a-f0-9]{64}$/),
      }),
    });
    /* eslint-enable @typescript-eslint/no-unsafe-assignment */
  });
});

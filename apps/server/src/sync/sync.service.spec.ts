import { ConfigService } from '@nestjs/config';
import { DeviceProfile, SiteType } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { DeviceAuthService } from './device-auth.service';
import { SyncService } from './sync.service';

describe('SyncService routing', () => {
  const siteId = '9cf1a57b-0e16-4e3b-b2f4-381ab3f5cff4';
  const deviceId = 'd86688f7-8206-48ee-854e-fdc3af266b17';
  const findMany = jest.fn().mockResolvedValue([]);
  const authenticate = jest.fn();
  const service = new SyncService(
    { syncEvent: { findMany } } as unknown as PrismaService,
    { authenticate } as unknown as DeviceAuthService,
    { getOrThrow: () => 'test-only-cursor-signing-key' } as unknown as ConfigService,
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
            { siteId },
            {
              eventType: 'kitchen_request.submitted',
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
            { siteId },
            {
              eventType: 'shipment.dispatched',
              site: { type: SiteType.KITCHEN },
              payload: { path: ['destination_site_id'], equals: siteId },
            },
          ],
          serverPosition: { gt: 0n },
        },
      }),
    );
  });
});

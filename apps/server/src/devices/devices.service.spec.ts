import { DeviceProfile, EnrollmentStatus } from '@prisma/client';
import { DevicesService, safeDeviceSelect } from './devices.service';

const enrolledDevice = {
  id: '00000000-0000-0000-0000-000000000001',
  siteId: '00000000-0000-0000-0000-000000000002',
  profile: DeviceProfile.BRANCH_TYPE_1,
  enrollmentStatus: EnrollmentStatus.ENROLLED,
  keyThumbprint: 'thumbprint',
  activeWriter: true,
  lastSeenAt: new Date(),
  appVersion: '1.2.6',
  streamEpoch: 1,
  name: 'BRANCH-PC',
  createdAt: new Date(),
  updatedAt: new Date(),
};

describe('DevicesService revoke', () => {
  it('revokes the device, clears its authentication material, and releases the writer slot', async () => {
    const update = jest.fn().mockResolvedValue({
      ...enrolledDevice,
      enrollmentStatus: EnrollmentStatus.REVOKED,
      activeWriter: false,
      keyThumbprint: null,
      streamEpoch: 2,
    });
    const transaction = {
      device: { findUnique: jest.fn().mockResolvedValue(enrolledDevice), update },
    };
    const prisma = { $transaction: jest.fn((operation: (client: typeof transaction) => unknown) => operation(transaction)) };

    const result = await new DevicesService(prisma as never).revoke(enrolledDevice.id);

    expect(update).toHaveBeenCalledWith({
      where: { id: enrolledDevice.id },
      data: {
        enrollmentStatus: EnrollmentStatus.REVOKED,
        activeWriter: false,
        credentialHash: null,
        keyThumbprint: null,
        streamEpoch: { increment: 1 },
      },
      select: safeDeviceSelect,
    });
    expect(result.enrollmentStatus).toBe(EnrollmentStatus.REVOKED);
    expect(result.activeWriter).toBe(false);
  });

  it('is idempotent for an already revoked device', async () => {
    const revoked = { ...enrolledDevice, enrollmentStatus: EnrollmentStatus.REVOKED, activeWriter: false, keyThumbprint: null, credentialHash: null };
    const transaction = {
      device: { findUnique: jest.fn().mockResolvedValue(revoked), update: jest.fn() },
    };
    const prisma = { $transaction: jest.fn((operation: (client: typeof transaction) => unknown) => operation(transaction)) };
    const { credentialHash: _credentialHash, ...safeRevoked } = revoked;

    await expect(new DevicesService(prisma as never).revoke(revoked.id)).resolves.toEqual(safeRevoked);
    expect(transaction.device.update).not.toHaveBeenCalled();
  });

  it('cleans stale authentication material from a legacy revoked device without advancing its epoch', async () => {
    const revoked = { ...enrolledDevice, enrollmentStatus: EnrollmentStatus.REVOKED, activeWriter: false,
      keyThumbprint: 'legacy-thumbprint', credentialHash: null };
    const { credentialHash: _credentialHash, ...safeRevoked } = revoked;
    const cleaned = { ...safeRevoked, keyThumbprint: null };
    const update = jest.fn().mockResolvedValue(cleaned);
    const transaction = { device: { findUnique: jest.fn().mockResolvedValue(revoked), update } };
    const prisma = { $transaction: jest.fn((operation: (client: typeof transaction) => unknown) => operation(transaction)) };

    await expect(new DevicesService(prisma as never).revoke(revoked.id)).resolves.toEqual(cleaned);
    expect(update).toHaveBeenCalledWith({
      where: { id: revoked.id },
      data: { activeWriter: false, credentialHash: null, keyThumbprint: null },
      select: safeDeviceSelect,
    });
    expect(update.mock.calls[0][0].data).not.toHaveProperty('streamEpoch');
  });

  it('does not allow disabling an internal server synchronization identity', async () => {
    const systemDevice = { ...enrolledDevice, name: '__SERVER_SYNC__', activeWriter: false };
    const transaction = {
      device: { findUnique: jest.fn().mockResolvedValue(systemDevice), update: jest.fn() },
    };
    const prisma = { $transaction: jest.fn((operation: (client: typeof transaction) => unknown) => operation(transaction)) };

    await expect(new DevicesService(prisma as never).revoke(systemDevice.id))
      .rejects.toThrow('System synchronization identities cannot be disabled');
    expect(transaction.device.update).not.toHaveBeenCalled();
  });
});

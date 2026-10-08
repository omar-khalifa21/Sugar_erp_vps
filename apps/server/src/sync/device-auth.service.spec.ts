import { DeviceAuthService } from './device-auth.service';
import { hashSecret } from '../enrollment/enrollment.service';

describe('Device authentication', () => {
  const id = '10000000-0000-4000-8000-000000000001';
  const valid = { id, activeWriter: true, enrollmentStatus: 'ENROLLED', credentialHash: hashSecret('synthetic-secret'), site: { active: true } };
  it.each([{ ...valid, activeWriter: false }, { ...valid, site: { active: false } }, { ...valid, enrollmentStatus: 'REVOKED' }])('blocks an inactive/revoked writer or site', async (device) => {
    const update = jest.fn();
    const auth = new DeviceAuthService({ device: { findUnique: jest.fn().mockResolvedValue(device), update } } as never);
    await expect(auth.authenticate(id, 'synthetic-secret')).rejects.toThrow('Device credentials are invalid');
    expect(update).not.toHaveBeenCalled();
  });
});

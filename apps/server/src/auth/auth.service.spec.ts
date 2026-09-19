import * as argon2 from 'argon2';
import { Prisma } from '@prisma/client';
import { AuthService } from './auth.service';

describe('AuthService signup', () => {
  it('hashes the password and creates a permissionless pending account', async () => {
    let saved: Prisma.UserCreateInput | undefined;
    const create = jest.fn((args: { data: Prisma.UserCreateInput }) => { saved = args.data; return Promise.resolve({ id: 'new-user' }); });
    const prisma = { user: { findUnique: jest.fn().mockResolvedValue(null), create } };
    const service = new AuthService(prisma as never, {} as never);

    await expect(service.signup({ username: ' NewUser ', displayName: ' New User ', password: 'long-password-123' }))
      .resolves.toEqual({ status: 'PENDING_PERMISSION' });

    expect(create).toHaveBeenCalledTimes(1);
    if (!saved) throw new Error('User was not saved');
    expect(saved).toMatchObject({ username: 'newuser', displayName: 'New User', status: 'PENDING_PERMISSION' });
    expect(saved.siteRoles).toBeUndefined();
    expect(saved.passwordHash).not.toBe('long-password-123');
    expect(await argon2.verify(saved.passwordHash, 'long-password-123')).toBe(true);
  });
});

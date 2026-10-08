import * as argon2 from 'argon2';
import { Prisma } from '@prisma/client';
import { AuthService } from './auth.service';
import { JwtStrategy } from './jwt.strategy';

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

describe('Account authentication', () => {
  const password = 'synthetic-password-123';
  let passwordHash: string;
  beforeAll(async () => { passwordHash = await argon2.hash(password); });

  it.each(['PENDING_PERMISSION', 'DISABLED'])('never issues a token for %s', async (status) => {
    const signAsync = jest.fn();
    const service = new AuthService({ user: { findUnique: jest.fn().mockResolvedValue({ id: 'user', active: true, status, passwordHash }) } } as never, { signAsync } as never);
    await expect(service.login({ username: 'user', password })).rejects.toThrow(status === 'PENDING_PERMISSION' ? 'Your account is awaiting approval' : 'Invalid username or password');
    expect(signAsync).not.toHaveBeenCalled();
  });

  it.each([null, 'PENDING_PERMISSION', 'ACTIVE', 'DISABLED'])('does not reveal account state with an incorrect password (%s)', async (status) => {
    const service = new AuthService({ user: { findUnique: jest.fn().mockResolvedValue(status ? { active: true, status, passwordHash } : null) } } as never, {} as never);
    await expect(service.login({ username: 'someone', password: 'incorrect-password' })).rejects.toThrow('Invalid username or password');
  });

  it('returns the same signup response on a duplicate without changing the account', async () => {
    const service = new AuthService({ user: { create: jest.fn().mockRejectedValue(new Prisma.PrismaClientKnownRequestError('duplicate', { code: 'P2002', clientVersion: 'test' })) } } as never, {} as never);
    await expect(service.signup({ username: 'someone', displayName: 'Someone', password })).resolves.toEqual({ status: 'PENDING_PERMISSION' });
  });

  it('checks approval and token revocation on every bearer request, including legacy tokens', async () => {
    const findUnique = jest.fn();
    const strategy = new JwtStrategy({ getOrThrow: () => 'synthetic-jwt-secret-at-least-32-characters' } as never, { user: { findUnique } } as never);
    const payload = { sub: '10000000-0000-4000-8000-000000000001' };
    for (const user of [null, { active: true, status: 'PENDING_PERMISSION', tokenVersion: 0 }, { active: false, status: 'ACTIVE', tokenVersion: 0 }, { active: true, status: 'ACTIVE', tokenVersion: 1 }]) {
      findUnique.mockResolvedValue(user);
      await expect(strategy.validate(payload)).rejects.toThrow();
    }
    findUnique.mockResolvedValue({ id: payload.sub, active: true, status: 'ACTIVE', tokenVersion: 0, siteRoles: [] });
    await expect(strategy.validate(payload)).resolves.toEqual({ id: payload.sub, roles: [] });
  });
});

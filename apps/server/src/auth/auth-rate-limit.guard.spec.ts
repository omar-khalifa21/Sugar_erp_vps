import { AuthRateLimitGuard } from './auth-rate-limit.guard';

describe('Authentication rate limiting', () => {
  const context = (path: string, ip = '127.0.0.1') => ({ switchToHttp: () => ({
    getRequest: () => ({ path, ip, socket: {}, headers: { 'x-forwarded-for': Math.random().toString() } }),
    getResponse: () => ({ setHeader: jest.fn() }),
  }) });

  it('limits login and signup independently and ignores spoofed forwarding headers', () => {
    const guard = new AuthRateLimitGuard();
    for (let attempt = 0; attempt < 20; attempt++) expect(guard.canActivate(context('/auth/login') as never)).toBe(true);
    expect(() => guard.canActivate(context('/auth/login') as never)).toThrow('Too many attempts');
    for (let attempt = 0; attempt < 5; attempt++) expect(guard.canActivate(context('/auth/signup') as never)).toBe(true);
    expect(() => guard.canActivate(context('/auth/signup') as never)).toThrow('Too many attempts');
    expect(guard.canActivate(context('/auth/login', '127.0.0.2') as never)).toBe(true);
  });

  it('allows requests after the window expires', () => {
    const now = jest.spyOn(Date, 'now').mockReturnValue(0);
    const guard = new AuthRateLimitGuard();
    try {
      for (let attempt = 0; attempt < 20; attempt++) guard.canActivate(context('/auth/login') as never);
      now.mockReturnValue(60_001);
      expect(guard.canActivate(context('/auth/login') as never)).toBe(true);
    } finally { now.mockRestore(); }
  });

  it('shares the signup quota across accepted casing and trailing-slash variants', () => {
    const guard = new AuthRateLimitGuard();
    for (let attempt = 0; attempt < 5; attempt++) guard.canActivate(context('/auth/signup') as never);
    expect(() => guard.canActivate(context('/auth/SIGNUP/') as never)).toThrow('Too many attempts');
  });
});

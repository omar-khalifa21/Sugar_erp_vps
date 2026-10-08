import { CanActivate, ExecutionContext, HttpException, Injectable } from '@nestjs/common';
import type { Request, Response } from 'express';

@Injectable()
export class AuthRateLimitGuard implements CanActivate {
  private readonly attempts = new Map<string, { count: number; expires: number }>();

  canActivate(context: ExecutionContext): boolean {
    const request = context.switchToHttp().getRequest<Request>();
    const response = context.switchToHttp().getResponse<Response>();
    const signup = /\/signup\/?$/i.test(request.path);
    const windowMs = signup ? 600_000 : 60_000;
    const limit = signup ? 5 : 20;
    const now = Date.now();
    // request.ip trusts only the explicitly configured reverse proxy. Never read
    // arbitrary X-Forwarded-For directly. Bound storage even under address churn.
    const key = `${signup ? 'signup' : 'login'}:${request.ip ?? request.socket.remoteAddress}`;
    for (const [storedKey, value] of this.attempts) {
      if (value.expires <= now) this.attempts.delete(storedKey);
    }
    const existing = this.attempts.get(key);
    if ((!existing && this.attempts.size >= 10_000) || (existing && existing.count >= limit)) {
      response.setHeader('Retry-After', Math.max(1, Math.ceil(((existing?.expires ?? now + windowMs) - now) / 1000)));
      throw new HttpException({ code: 'RATE_LIMITED', message: 'Too many attempts. Please try again later.', retryable: true }, 429);
    }
    this.attempts.set(key, { count: (existing?.count ?? 0) + 1, expires: existing?.expires ?? now + windowMs });
    return true;
  }
}

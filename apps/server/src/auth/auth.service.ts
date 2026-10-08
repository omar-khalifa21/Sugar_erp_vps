import { Injectable, UnauthorizedException, ForbiddenException } from '@nestjs/common';
import { createHash, randomBytes } from 'node:crypto';
import { JwtService } from '@nestjs/jwt';
import * as argon2 from 'argon2';
import { Prisma } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { LoginDto } from './login.dto';
import { SignupDto } from './signup.dto';

@Injectable()
export class AuthService {
  private readonly dummyHash = argon2.hash(randomBytes(32));
  constructor(
    private readonly prisma: PrismaService,
    private readonly jwt: JwtService,
  ) {}

  async session(userId: string) {
    const user = await this.prisma.user.findUnique({ where: { id: userId },
      select: { id: true, username: true, displayName: true, status: true, active: true, siteRoles: { where: { role: { active: true } },
        select: { siteId: true, role: { select: { code: true, permissions: true } } } } } });
    if (!user?.active || user.status !== 'ACTIVE') throw new UnauthorizedException();
    return user;
  }

  async desktopAuthorization(userId: string, deviceId: string, credential: string) {
    if (!deviceId || !credential || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(deviceId)) throw new UnauthorizedException();
    const device = await this.prisma.device.findFirst({ where: { id: deviceId, activeWriter: true, enrollmentStatus: 'ENROLLED',
      credentialHash: createHash('sha256').update(credential).digest('hex'), site: { active: true } } });
    if (!device) throw new UnauthorizedException();
    const user = await this.session(userId);
    const permissions = [...new Set(user.siteRoles.filter((assignment) => !assignment.siteId || assignment.siteId === device.siteId).flatMap((assignment) => assignment.role.permissions))];
    if (!permissions.some((permission) => ['*', 'inventory.write'].includes(permission))) throw new ForbiddenException('No operational permission at this site');
    // This proof has no `sub`: it cannot be used as a normal API bearer token.
    const authorization = await this.jwt.signAsync({ purpose: 'desktop-operation', user_id: userId, device_id: device.id,
      site_id: device.siteId, permissions }, { expiresIn: '15m' });
    return { user_id: userId, site_id: device.siteId, device_id: device.id, authorization, expires_in_seconds: 900 };
  }

  async signup(input: SignupDto): Promise<{ status: 'PENDING_PERMISSION' }> {
    const username = input.username.trim().toLowerCase();
    // Hash on both duplicate and new requests to avoid a cheap enumeration path.
    const passwordHash = await argon2.hash(input.password);
    try {
      await this.prisma.user.create({ data: {
        username,
        displayName: input.displayName.trim(),
        passwordHash,
        status: 'PENDING_PERMISSION',
      } });
    } catch (error) {
      if (error instanceof Prisma.PrismaClientKnownRequestError && error.code === 'P2002') {
        return { status: 'PENDING_PERMISSION' };
      }
      throw error;
    }
    return { status: 'PENDING_PERMISSION' };
  }

  async login(input: LoginDto): Promise<{ access_token: string; token_type: 'Bearer'; status: string }> {
    const username = input.username.trim().toLowerCase();
    const user = await this.prisma.user.findUnique({ where: { username } });
    const validPassword = await argon2.verify(user?.passwordHash ?? await this.dummyHash, input.password);
    if (!user || !validPassword || !user.active || user.status === 'DISABLED') {
      throw new UnauthorizedException('Invalid username or password');
    }
    if (user.status !== 'ACTIVE') {
      throw new ForbiddenException({ code: 'ACCOUNT_PENDING', message: 'Your account is awaiting approval' });
    }
    return {
      access_token: await this.jwt.signAsync({ sub: user.id, ver: user.tokenVersion }),
      token_type: 'Bearer',
      status: user.status,
    };
  }
}

import { Injectable, UnauthorizedException } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { PassportStrategy } from '@nestjs/passport';
import { ExtractJwt, Strategy } from 'passport-jwt';
import { PrismaService } from '../prisma/prisma.service';

interface JwtPayload {
  sub: string;
}

@Injectable()
export class JwtStrategy extends PassportStrategy(Strategy) {
  constructor(config: ConfigService, private readonly prisma: PrismaService) {
    super({
      jwtFromRequest: ExtractJwt.fromAuthHeaderAsBearerToken(),
      ignoreExpiration: false,
      secretOrKey: config.getOrThrow<string>('JWT_SECRET'),
    });
  }

  async validate(payload: JwtPayload): Promise<{ id: string; roles: string[] }> {
    const user = await this.prisma.user.findUnique({
      where: { id: payload.sub },
      include: { siteRoles: { include: { role: true } } },
    });
    if (!user?.active) throw new UnauthorizedException();
    return { id: user.id, roles: user.siteRoles.map((assignment) => assignment.role.code) };
  }
}

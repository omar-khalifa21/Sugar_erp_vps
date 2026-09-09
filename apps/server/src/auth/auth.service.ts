import { Injectable, UnauthorizedException } from '@nestjs/common';
import { JwtService } from '@nestjs/jwt';
import * as argon2 from 'argon2';
import { PrismaService } from '../prisma/prisma.service';
import { LoginDto } from './login.dto';

@Injectable()
export class AuthService {
  constructor(
    private readonly prisma: PrismaService,
    private readonly jwt: JwtService,
  ) {}

  async login(input: LoginDto): Promise<{ access_token: string; token_type: 'Bearer' }> {
    const username = input.username.trim().toLowerCase();
    const user = await this.prisma.user.findUnique({ where: { username } });
    if (!user?.active || !(await argon2.verify(user.passwordHash, input.password))) {
      throw new UnauthorizedException('Invalid username or password');
    }
    return {
      access_token: await this.jwt.signAsync({ sub: user.id }),
      token_type: 'Bearer',
    };
  }
}

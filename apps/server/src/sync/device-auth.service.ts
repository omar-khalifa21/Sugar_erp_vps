import { Injectable, UnauthorizedException } from '@nestjs/common';
import { EnrollmentStatus } from '@prisma/client';
import { timingSafeEqual } from 'node:crypto';
import { hashSecret } from '../enrollment/enrollment.service';
import { PrismaService } from '../prisma/prisma.service';

@Injectable()
export class DeviceAuthService {
  constructor(private readonly prisma: PrismaService) {}

  async authenticate(deviceId?: string, credential?: string) {
    if (!deviceId || !credential) throw new UnauthorizedException('Device credentials are required');
    const device = await this.prisma.device.findUnique({ where: { id: deviceId } });
    const actual = Buffer.from(device?.credentialHash || '', 'utf8');
    const expected = Buffer.from(hashSecret(credential), 'utf8');
    if (
      !device ||
      device.enrollmentStatus !== EnrollmentStatus.ENROLLED ||
      !device.credentialHash ||
      actual.length !== expected.length ||
      !timingSafeEqual(actual, expected)
    ) {
      throw new UnauthorizedException('Device credentials are invalid');
    }
    await this.prisma.device.update({ where: { id: device.id }, data: { lastSeenAt: new Date() } });
    return device;
  }
}

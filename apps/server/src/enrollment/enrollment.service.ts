import {
  ConflictException,
  HttpException,
  Injectable,
  NotFoundException,
  UnauthorizedException,
} from '@nestjs/common';
import { DeviceProfile, EnrollmentStatus, Prisma, SiteType } from '@prisma/client';
import { createHash, randomBytes } from 'node:crypto';
import { safeDeviceSelect, type SafeDevice } from '../devices/devices.service';
import { PrismaService } from '../prisma/prisma.service';
import { EnrollDeviceDto } from './enroll-device.dto';
import { IssueEnrollmentTokenDto } from './issue-enrollment-token.dto';

const profileForType: Record<SiteType, DeviceProfile> = {
  BRANCH_TYPE_1: DeviceProfile.BRANCH_TYPE_1,
  BRANCH_TYPE_2: DeviceProfile.BRANCH_TYPE_2,
  KITCHEN: DeviceProfile.KITCHEN,
};

@Injectable()
export class EnrollmentService {
  constructor(private readonly prisma: PrismaService) {}

  async issue(siteId: string, userId: string, input: IssueEnrollmentTokenDto) {
    const site = await this.prisma.site.findUnique({ where: { id: siteId } });
    if (!site?.active) throw new NotFoundException('Active site not found');
    const token = randomBytes(32).toString('base64url');
    const expiresAt = new Date(Date.now() + (input.expiresInMinutes ?? 30) * 60_000);
    const record = await this.prisma.enrollmentToken.create({
      data: {
        siteId,
        profile: profileForType[site.type],
        tokenHash: hashSecret(token),
        expiresAt,
        createdByUserId: userId,
      },
    });
    return {
      id: record.id,
      token,
      siteId,
      profile: record.profile,
      expiresAt: record.expiresAt,
    };
  }

  async enroll(input: EnrollDeviceDto): Promise<{
    device: SafeDevice;
    credential: string;
    contractVersion: '1.0';
  }> {
    const tokenHash = hashSecret(input.token);
    const token = await this.prisma.enrollmentToken.findUnique({
      where: { tokenHash },
      include: { site: true },
    });
    if (!token || token.usedAt || token.expiresAt <= new Date() || !token.site.active) {
      throw new UnauthorizedException('Enrollment token is invalid or expired');
    }
    if (input.expectedProfile && input.expectedProfile !== token.profile) {
      throw new ConflictException('Enrollment token belongs to a different application profile');
    }
    const credential = randomBytes(32).toString('base64url');

    try {
      return await this.prisma.$transaction(
        async (transaction) => {
          const consumed = await transaction.enrollmentToken.updateMany({
            where: { id: token.id, usedAt: null, expiresAt: { gt: new Date() } },
            data: { usedAt: new Date() },
          });
          if (consumed.count !== 1) {
            throw new ConflictException('Enrollment token was already used');
          }
          const existingWriter = await transaction.device.findFirst({
            where: {
              siteId: token.siteId,
              activeWriter: true,
              enrollmentStatus: EnrollmentStatus.ENROLLED,
            },
          });
          if (existingWriter) {
            throw new HttpException(
              {
                code: 'ACTIVE_WRITER_EXISTS',
                message: 'This site already has an active stock-writing device',
                retryable: false,
              },
              409,
            );
          }
          const device = await transaction.device.create({
            data: {
              siteId: token.siteId,
              profile: token.profile,
              name: input.deviceName.trim(),
              keyThumbprint: input.keyThumbprint.toLowerCase(),
              credentialHash: hashSecret(credential),
              enrollmentStatus: EnrollmentStatus.ENROLLED,
              activeWriter: true,
              appVersion: input.appVersion,
              lastSeenAt: new Date(),
              syncState: { create: {} },
              syncCursor: { create: {} },
            },
            select: safeDeviceSelect,
          });
          await transaction.enrollmentToken.update({
            where: { id: token.id },
            data: { usedByDeviceId: device.id },
          });
          return { device, credential, contractVersion: '1.0' as const };
        },
        { isolationLevel: Prisma.TransactionIsolationLevel.Serializable },
      );
    } catch (error) {
      if (error instanceof Prisma.PrismaClientKnownRequestError && error.code === 'P2002') {
        throw new ConflictException('Device identity or active writer already exists');
      }
      throw error;
    }
  }
}

export function hashSecret(value: string): string {
  return createHash('sha256').update(value, 'utf8').digest('hex');
}

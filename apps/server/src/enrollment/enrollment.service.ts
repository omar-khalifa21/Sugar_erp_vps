import {
  ConflictException,
  HttpException,
  Injectable,
  NotFoundException,
  UnauthorizedException,
} from '@nestjs/common';
import { DeviceProfile, EnrollmentStatus, Prisma, SiteType } from '@prisma/client';
import * as argon2 from 'argon2';
import { createHash, randomBytes } from 'node:crypto';
import { safeDeviceSelect, type SafeDevice } from '../devices/devices.service';
import { PrismaService } from '../prisma/prisma.service';
import { KitchenPasswordEnrollmentDto, SetKitchenPasswordDto } from './kitchen-password.dto';
import { EnrollmentDeviceInfo, EnrollDeviceDto } from './enroll-device.dto';
import { IssueEnrollmentTokenDto } from './issue-enrollment-token.dto';

const profileForType: Record<SiteType, DeviceProfile> = {
  BRANCH_TYPE_1: DeviceProfile.BRANCH_TYPE_1,
  BRANCH_TYPE_2: DeviceProfile.BRANCH_TYPE_2,
  KITCHEN: DeviceProfile.KITCHEN,
};

@Injectable()
export class EnrollmentService {
  private readonly dummyHash = argon2.hash(randomBytes(32));

  constructor(private readonly prisma: PrismaService) {}

  kitchenPasswordStatus() {
    return this.prisma.kitchenEnrollmentSetting.findUnique({
      where: { id: 1 }, select: { siteId: true, updatedAt: true },
    });
  }

  async setKitchenPassword(input: SetKitchenPasswordDto, actorId: string) {
    const passwordHash = await argon2.hash(input.password, { type: argon2.argon2id });
    return this.prisma.$transaction(async (tx) => {
      const site = await tx.site.findFirst({ where: { id: input.siteId, type: SiteType.KITCHEN, active: true } });
      if (!site) throw new NotFoundException('Active kitchen site not found');
      const values = { siteId: site.id, passwordHash, updatedBy: actorId };
      return tx.kitchenEnrollmentSetting.upsert({
        where: { id: 1 }, create: { id: 1, ...values }, update: values,
        select: { siteId: true, updatedAt: true },
      });
    });
  }

  async enrollKitchen(input: KitchenPasswordEnrollmentDto) {
    const setting = await this.prisma.kitchenEnrollmentSetting.findUnique({ where: { id: 1 } });
    const valid = await argon2.verify(setting?.passwordHash ?? await this.dummyHash, input.password);
    if (!setting || !valid) throw new UnauthorizedException('Enrollment unavailable');

    return this.writerTransaction(async (tx) => {
      // Lock the verified setting until issuance commits so rotation cannot authorize an old password.
      const verified = await tx.$queryRaw<{ site_id: string }[]>`
        SELECT k.site_id FROM kitchen_enrollment_settings k JOIN sites s ON s.id = k.site_id
        WHERE k.id = 1 AND k.password_hash = ${setting.passwordHash}
          AND s.active AND s.type = 'KITCHEN'
        FOR SHARE OF k, s`;
      if (!verified.length) throw new UnauthorizedException('Enrollment unavailable');
      return this.createWriter(tx, verified[0].site_id, DeviceProfile.KITCHEN, input);
    });
  }

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
    return this.writerTransaction(async (transaction) => {
      const consumed = await transaction.enrollmentToken.updateMany({
        where: { id: token.id, usedAt: null, expiresAt: { gt: new Date() } },
        data: { usedAt: new Date() },
      });
      if (consumed.count !== 1) {
        throw new ConflictException('Enrollment token was already used');
      }
      const result = await this.createWriter(transaction, token.siteId, token.profile, input);
      await transaction.enrollmentToken.update({
        where: { id: token.id },
        data: { usedByDeviceId: result.device.id },
      });
      return result;
    });
  }

  private async createWriter(transaction: Prisma.TransactionClient, siteId: string, profile: DeviceProfile, input: EnrollmentDeviceInfo) {
    const credential = randomBytes(32).toString('base64url');
    const existingWriter = await transaction.device.findFirst({
      where: {
        siteId,
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
        siteId,
        profile,
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
    return { device, credential, contractVersion: '1.0' as const };
  }

  private async writerTransaction<T>(write: (tx: Prisma.TransactionClient) => Promise<T>) {
    try {
      return await this.prisma.$transaction(write, { isolationLevel: Prisma.TransactionIsolationLevel.Serializable });
    } catch (error) {
      if (error instanceof Prisma.PrismaClientKnownRequestError && ['P2002', 'P2034'].includes(error.code)) {
        throw new ConflictException('Device identity or active writer already exists');
      }
      throw error;
    }
  }
}

export function hashSecret(value: string): string {
  return createHash('sha256').update(value, 'utf8').digest('hex');
}

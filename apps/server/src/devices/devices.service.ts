import { Injectable, NotFoundException, UnprocessableEntityException } from '@nestjs/common';
import { EnrollmentStatus, Prisma } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { CreateDeviceDto } from './create-device.dto';

@Injectable()
export class DevicesService {
  constructor(private readonly prisma: PrismaService) {}

  list(): Promise<SafeDevice[]> {
    return this.prisma.device.findMany({ select: safeDeviceSelect, orderBy: { createdAt: 'desc' } });
  }

  async create(input: CreateDeviceDto): Promise<SafeDevice> {
    const site = await this.prisma.site.findUniqueOrThrow({ where: { id: input.siteId } });
    if (site.type !== input.profile) {
      throw new UnprocessableEntityException('Device profile must match the site type');
    }
    return this.prisma.device.create({
      data: {
        siteId: input.siteId,
        profile: input.profile,
        activeWriter: input.activeWriter || false,
        enrollmentStatus: EnrollmentStatus.PENDING,
      },
      select: safeDeviceSelect,
    });
  }

  async revoke(id: string): Promise<SafeDevice> {
    return this.prisma.$transaction(async (transaction) => {
      const device = await transaction.device.findUnique({ where: { id }, select: { ...safeDeviceSelect, credentialHash: true } });
      if (!device) throw new NotFoundException('Device not found');
      if (device.name === '__SERVER_SYNC__') {
        throw new UnprocessableEntityException('System synchronization identities cannot be disabled');
      }

      // Revocation is deliberately idempotent. Repeating an admin request must not
      // keep advancing the stream epoch, but the first request invalidates the
      // credential and releases the site's single active-writer slot atomically.
      if (device.enrollmentStatus === EnrollmentStatus.REVOKED) {
        const { credentialHash, ...safeDevice } = device;
        if (!credentialHash && !device.keyThumbprint && !device.activeWriter) return safeDevice;
        return transaction.device.update({
          where: { id },
          data: { activeWriter: false, credentialHash: null, keyThumbprint: null },
          select: safeDeviceSelect,
        });
      }

      return transaction.device.update({
        where: { id },
        data: {
          enrollmentStatus: EnrollmentStatus.REVOKED,
          activeWriter: false,
          credentialHash: null,
          keyThumbprint: null,
          streamEpoch: { increment: 1 },
        },
        select: safeDeviceSelect,
      });
    });
  }
}

export const safeDeviceSelect = {
  id: true,
  siteId: true,
  profile: true,
  enrollmentStatus: true,
  keyThumbprint: true,
  activeWriter: true,
  lastSeenAt: true,
  appVersion: true,
  streamEpoch: true,
  name: true,
  createdAt: true,
  updatedAt: true,
} satisfies Prisma.DeviceSelect;

export type SafeDevice = Prisma.DeviceGetPayload<{ select: typeof safeDeviceSelect }>;

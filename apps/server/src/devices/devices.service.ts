import { Injectable, UnprocessableEntityException } from '@nestjs/common';
import { Device, EnrollmentStatus } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { CreateDeviceDto } from './create-device.dto';

@Injectable()
export class DevicesService {
  constructor(private readonly prisma: PrismaService) {}

  list(): Promise<Device[]> {
    return this.prisma.device.findMany({ orderBy: { createdAt: 'desc' } });
  }

  async create(input: CreateDeviceDto): Promise<Device> {
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
    });
  }
}

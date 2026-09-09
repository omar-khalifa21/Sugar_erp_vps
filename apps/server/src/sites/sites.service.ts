import { Injectable } from '@nestjs/common';
import { Site } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { CreateSiteDto } from './create-site.dto';
import { UpdateSiteDto } from './update-site.dto';

@Injectable()
export class SitesService {
  constructor(private readonly prisma: PrismaService) {}

  list(): Promise<Site[]> {
    return this.prisma.site.findMany({ orderBy: { code: 'asc' } });
  }

  create(input: CreateSiteDto): Promise<Site> {
    return this.prisma.site.create({
      data: {
        code: input.code.trim().toUpperCase(),
        name: input.name.trim(),
        type: input.type,
        timezone: input.timezone || 'Africa/Cairo',
      },
    });
  }

  update(id: string, input: UpdateSiteDto): Promise<Site> {
    return this.prisma.site.update({
      where: { id },
      data: {
        ...(input.code !== undefined ? { code: input.code.trim().toUpperCase() } : {}),
        ...(input.name !== undefined ? { name: input.name.trim() } : {}),
        ...(input.type !== undefined ? { type: input.type } : {}),
        ...(input.timezone !== undefined ? { timezone: input.timezone.trim() } : {}),
        ...(input.active !== undefined ? { active: input.active } : {}),
      },
    });
  }

  archive(id: string): Promise<Site> {
    return this.prisma.site.update({ where: { id }, data: { active: false } });
  }
}

import { Injectable } from '@nestjs/common';
import { Site } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { CreateSiteDto } from './create-site.dto';

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
}

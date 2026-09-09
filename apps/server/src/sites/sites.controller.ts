import { Body, Controller, Get, Post } from '@nestjs/common';
import { Site } from '@prisma/client';
import { CreateSiteDto } from './create-site.dto';
import { SitesService } from './sites.service';

@Controller('sites')
export class SitesController {
  constructor(private readonly sites: SitesService) {}

  @Get()
  list(): Promise<Site[]> {
    return this.sites.list();
  }

  @Post()
  create(@Body() input: CreateSiteDto): Promise<Site> {
    return this.sites.create(input);
  }
}

import { Body, Controller, Delete, Get, Param, ParseUUIDPipe, Patch, Post } from '@nestjs/common';
import { Site } from '@prisma/client';
import { CreateSiteDto } from './create-site.dto';
import { SitesService } from './sites.service';
import { Roles } from '../common/roles.decorator';
import { UpdateSiteDto } from './update-site.dto';

@Roles('ADMIN')
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

  @Patch(':id')
  update(@Param('id', ParseUUIDPipe) id: string, @Body() input: UpdateSiteDto): Promise<Site> {
    return this.sites.update(id, input);
  }

  @Delete(':id')
  archive(@Param('id', ParseUUIDPipe) id: string): Promise<Site> {
    return this.sites.archive(id);
  }
}

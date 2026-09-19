import { Body, Controller, Get, Headers, HttpCode, Param, ParseUUIDPipe, Post, Query } from '@nestjs/common';
import { Public } from '../common/public.decorator';
import { Roles } from '../common/roles.decorator';
import { SyncAckDto, SyncPullQueryDto, SyncPushDto } from './sync.dto';
import { SyncService } from './sync.service';

@Public()
@Controller('sync')
export class SyncController {
  constructor(private readonly sync: SyncService) {}

  @Get('bootstrap')
  bootstrap(@Headers('x-device-id') deviceId: string | undefined, @Headers('x-device-secret') credential: string | undefined, @Headers('x-app-version') appVersion: string | undefined) {
    return this.sync.bootstrap(deviceId, credential, appVersion);
  }

  @Post('push')
  @HttpCode(200)
  push(
    @Headers('x-device-id') deviceId: string | undefined,
    @Headers('x-device-secret') credential: string | undefined,
    @Headers('x-app-version') appVersion: string | undefined,
    @Body() input: SyncPushDto,
  ) {
    return this.sync.push(deviceId, credential, input, appVersion);
  }

  @Get('pull')
  pull(
    @Headers('x-device-id') deviceId: string | undefined,
    @Headers('x-device-secret') credential: string | undefined,
    @Headers('x-app-version') appVersion: string | undefined,
    @Query() query: SyncPullQueryDto,
  ) {
    return this.sync.pull(deviceId, credential, query.cursor, query.limit, appVersion);
  }

  @Post('ack')
  @HttpCode(200)
  acknowledge(
    @Headers('x-device-id') deviceId: string | undefined,
    @Headers('x-device-secret') credential: string | undefined,
    @Headers('x-app-version') appVersion: string | undefined,
    @Body() input: SyncAckDto,
  ) {
    return this.sync.acknowledge(deviceId, credential, input.cursor, appVersion);
  }
}

@Roles('ADMIN')
@Controller('admin/kitchen')
export class KitchenRequestReadController {
  constructor(private readonly sync: SyncService) {}

  @Get('requests')
  requests() {
    return this.sync.listKitchenRequests();
  }

  @Get('shipments')
  shipments() {
    return this.sync.listShipments();
  }
}

@Roles('ADMIN')
@Controller('admin/sites/:siteId')
export class SiteEventReadController {
  constructor(private readonly sync: SyncService) {}

  @Get('sales')
  sales(@Param('siteId', ParseUUIDPipe) siteId: string) {
    return this.sync.listSiteSales(siteId);
  }

  @Get('shift-closes')
  shifts(@Param('siteId', ParseUUIDPipe) siteId: string) {
    return this.sync.listSiteShiftCloses(siteId);
  }
}

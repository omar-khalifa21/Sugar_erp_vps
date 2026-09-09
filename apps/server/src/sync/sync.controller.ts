import { Body, Controller, Get, Headers, HttpCode, Post, Query } from '@nestjs/common';
import { Public } from '../common/public.decorator';
import { SyncAckDto, SyncPullQueryDto, SyncPushDto } from './sync.dto';
import { SyncService } from './sync.service';

@Public()
@Controller('sync')
export class SyncController {
  constructor(private readonly sync: SyncService) {}

  @Post('push')
  @HttpCode(200)
  push(
    @Headers('x-device-id') deviceId: string | undefined,
    @Headers('x-device-secret') credential: string | undefined,
    @Body() input: SyncPushDto,
  ) {
    return this.sync.push(deviceId, credential, input);
  }

  @Get('pull')
  pull(
    @Headers('x-device-id') deviceId: string | undefined,
    @Headers('x-device-secret') credential: string | undefined,
    @Query() query: SyncPullQueryDto,
  ) {
    return this.sync.pull(deviceId, credential, query.cursor, query.limit);
  }

  @Post('ack')
  @HttpCode(200)
  acknowledge(
    @Headers('x-device-id') deviceId: string | undefined,
    @Headers('x-device-secret') credential: string | undefined,
    @Body() input: SyncAckDto,
  ) {
    return this.sync.acknowledge(deviceId, credential, input.cursor);
  }
}

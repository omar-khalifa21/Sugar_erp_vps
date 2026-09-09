import { Body, Controller, Get, Param, ParseUUIDPipe, Post } from '@nestjs/common';
import { CreateDeviceDto } from './create-device.dto';
import { DevicesService, SafeDevice } from './devices.service';
import { Roles } from '../common/roles.decorator';

@Roles('ADMIN')
@Controller('devices')
export class DevicesController {
  constructor(private readonly devices: DevicesService) {}

  @Get()
  list(): Promise<SafeDevice[]> {
    return this.devices.list();
  }

  @Post()
  create(@Body() input: CreateDeviceDto): Promise<SafeDevice> {
    return this.devices.create(input);
  }

  @Post(':id/revoke')
  revoke(@Param('id', ParseUUIDPipe) id: string): Promise<SafeDevice> {
    return this.devices.revoke(id);
  }
}

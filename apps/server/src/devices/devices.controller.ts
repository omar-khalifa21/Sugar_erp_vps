import { Body, Controller, Get, Post } from '@nestjs/common';
import { Device } from '@prisma/client';
import { CreateDeviceDto } from './create-device.dto';
import { DevicesService } from './devices.service';

@Controller('devices')
export class DevicesController {
  constructor(private readonly devices: DevicesService) {}

  @Get()
  list(): Promise<Device[]> {
    return this.devices.list();
  }

  @Post()
  create(@Body() input: CreateDeviceDto): Promise<Device> {
    return this.devices.create(input);
  }
}

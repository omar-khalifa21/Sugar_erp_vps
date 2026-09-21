import { Module } from '@nestjs/common';
import { DeviceAuthService } from '../sync/device-auth.service';
import { AdminReportsController, DeviceReportsController } from './reports.controller';
import { ReportsService } from './reports.service';

@Module({ controllers: [DeviceReportsController, AdminReportsController], providers: [ReportsService, DeviceAuthService] })
export class ReportsModule {}

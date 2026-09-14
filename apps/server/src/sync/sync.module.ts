import { Module } from '@nestjs/common';
import { DeviceAuthService } from './device-auth.service';
import { KitchenRequestReadController, SiteEventReadController, SyncController } from './sync.controller';
import { SyncService } from './sync.service';

@Module({ controllers: [SyncController, KitchenRequestReadController, SiteEventReadController], providers: [DeviceAuthService, SyncService] })
export class SyncModule {}

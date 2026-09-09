import { Module } from '@nestjs/common';
import { DeviceAuthService } from './device-auth.service';
import { SyncController } from './sync.controller';
import { SyncService } from './sync.service';

@Module({ controllers: [SyncController], providers: [DeviceAuthService, SyncService] })
export class SyncModule {}

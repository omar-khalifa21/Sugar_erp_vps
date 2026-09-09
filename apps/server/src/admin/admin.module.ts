import { Module } from '@nestjs/common';
import { AdminBusinessController, AdminController } from './admin.controller';
import { AdminService } from './admin.service';

@Module({ controllers: [AdminController, AdminBusinessController], providers: [AdminService] })
export class AdminModule {}

import { Module } from '@nestjs/common';
import { ReleasesController } from './releases.controller';
import { KitchenReleasesController } from './kitchen-releases.controller';
import { BranchTwoReleasesController } from './branch-two-releases.controller';

@Module({ controllers: [ReleasesController, KitchenReleasesController, BranchTwoReleasesController] })
export class ReleasesModule {}

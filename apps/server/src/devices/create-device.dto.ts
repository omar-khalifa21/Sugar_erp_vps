import { DeviceProfile } from '@prisma/client';
import { IsBoolean, IsEnum, IsOptional, IsUUID } from 'class-validator';

export class CreateDeviceDto {
  @IsUUID()
  siteId!: string;

  @IsEnum(DeviceProfile)
  profile!: DeviceProfile;

  @IsOptional()
  @IsBoolean()
  activeWriter?: boolean;
}

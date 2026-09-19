import { IsEnum, IsOptional, IsString, Length, Matches, MaxLength } from 'class-validator';
import { DeviceProfile } from '@prisma/client';

export class EnrollDeviceDto {
  @IsOptional()
  @IsEnum(DeviceProfile)
  expectedProfile?: DeviceProfile;
  @IsString()
  @Length(32, 200)
  token!: string;

  @IsString()
  @Length(2, 120)
  deviceName!: string;

  @IsString()
  @Matches(/^[0-9a-f]{64}$/i)
  keyThumbprint!: string;

  @IsString()
  @MaxLength(50)
  appVersion!: string;
}

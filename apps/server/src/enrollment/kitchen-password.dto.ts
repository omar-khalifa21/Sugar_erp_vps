import { IsString, IsUUID, Length } from 'class-validator';
import { EnrollmentDeviceInfo } from './enroll-device.dto';

export class KitchenPasswordEnrollmentDto extends EnrollmentDeviceInfo {
  @IsString()
  @Length(1, 128)
  password!: string;
}

export class SetKitchenPasswordDto {
  @IsUUID()
  siteId!: string;

  @IsString()
  @Length(12, 128)
  password!: string;
}

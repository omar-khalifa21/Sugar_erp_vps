import { IsOptional, IsUUID } from 'class-validator';

export class ApproveUserDto {
  @IsUUID()
  roleId!: string;

  @IsOptional()
  @IsUUID()
  siteId?: string;
}

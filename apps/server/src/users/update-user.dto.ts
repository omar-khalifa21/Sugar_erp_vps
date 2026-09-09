import { IsBoolean, IsOptional, IsString, IsUUID, Length, MinLength } from 'class-validator';

export class UpdateUserDto {
  @IsOptional()
  @IsString()
  @Length(1, 100)
  username?: string;

  @IsOptional()
  @IsString()
  @Length(1, 200)
  displayName?: string;

  @IsOptional()
  @IsString()
  @MinLength(12)
  password?: string;

  @IsOptional()
  @IsBoolean()
  active?: boolean;

  @IsOptional()
  @IsUUID()
  roleId?: string;

  @IsOptional()
  @IsUUID()
  siteId?: string;
}

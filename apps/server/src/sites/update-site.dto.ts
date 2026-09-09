import { SiteType } from '@prisma/client';
import { IsBoolean, IsEnum, IsOptional, IsString, Length } from 'class-validator';

export class UpdateSiteDto {
  @IsOptional()
  @IsString()
  @Length(1, 50)
  code?: string;

  @IsOptional()
  @IsString()
  @Length(1, 200)
  name?: string;

  @IsOptional()
  @IsEnum(SiteType)
  type?: SiteType;

  @IsOptional()
  @IsString()
  @Length(1, 100)
  timezone?: string;

  @IsOptional()
  @IsBoolean()
  active?: boolean;
}

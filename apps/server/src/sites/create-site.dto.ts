import { SiteType } from '@prisma/client';
import { IsEnum, IsOptional, IsString, Length } from 'class-validator';

export class CreateSiteDto {
  @IsString()
  @Length(1, 50)
  code!: string;

  @IsString()
  @Length(1, 200)
  name!: string;

  @IsEnum(SiteType)
  type!: SiteType;

  @IsOptional()
  @IsString()
  @Length(1, 100)
  timezone?: string;
}

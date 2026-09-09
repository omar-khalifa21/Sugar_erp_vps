import { ItemKind } from '@prisma/client';
import { IsBoolean, IsEnum, IsInt, IsOptional, IsString, Length, Max, Min } from 'class-validator';

export class UpdateItemDto {
  @IsOptional()
  @IsString()
  @Length(1, 100)
  sku?: string;

  @IsOptional()
  @IsString()
  @Length(1, 300)
  nameAr?: string;

  @IsOptional()
  @IsString()
  @Length(1, 30)
  unit?: string;

  @IsOptional()
  @IsInt()
  @Min(1)
  quantityScale?: number;

  @IsOptional()
  @IsInt()
  @Min(0)
  @Max(2_000_000_000)
  retailPriceMinor?: number;

  @IsOptional()
  @IsEnum(ItemKind)
  kind?: ItemKind;

  @IsOptional()
  @IsBoolean()
  active?: boolean;
}

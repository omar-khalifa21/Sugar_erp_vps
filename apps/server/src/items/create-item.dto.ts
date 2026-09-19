import { ItemKind } from '@prisma/client';
import { IsEnum, IsInt, IsOptional, IsString, Length, Max, Min } from 'class-validator';

export class CreateItemDto {
  @IsOptional()
  @IsString()
  @Length(1, 100)
  sku?: string;

  @IsString()
  @Length(1, 300)
  nameAr!: string;

  @IsString()
  @Length(1, 30)
  unit!: string;

  @IsInt()
  @Min(1)
  quantityScale!: number;

  @IsOptional()
  @IsInt()
  @Min(0)
  @Max(2_000_000_000)
  retailPriceMinor?: number;

  @IsEnum(ItemKind)
  kind!: ItemKind;
}

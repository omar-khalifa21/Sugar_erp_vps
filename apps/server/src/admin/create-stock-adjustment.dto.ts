import { StockLocation } from '@prisma/client';
import { IsEnum, IsInt, IsString, IsUUID, Length, Matches, Min } from 'class-validator';

export class CreateStockAdjustmentDto {
  @IsUUID()
  itemId!: string;

  @IsEnum(StockLocation)
  location!: StockLocation;

  @IsString()
  @Matches(/^-?[1-9]\d*$/)
  deltaScaled!: string;

  @IsString()
  @Length(3, 500)
  reason!: string;

  @IsInt()
  @Min(1)
  expectedVersion!: number;
}

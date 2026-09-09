import { ItemKind } from '@prisma/client';
import { IsEnum, IsInt, IsString, Length, Min } from 'class-validator';

export class CreateItemDto {
  @IsString()
  @Length(1, 100)
  sku!: string;

  @IsString()
  @Length(1, 300)
  nameAr!: string;

  @IsString()
  @Length(1, 30)
  unit!: string;

  @IsInt()
  @Min(1)
  quantityScale!: number;

  @IsEnum(ItemKind)
  kind!: ItemKind;
}

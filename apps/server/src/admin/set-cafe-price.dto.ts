import { IsInt, Max, Min } from 'class-validator';

export class SetCafePriceDto {
  @IsInt()
  @Min(0)
  @Max(2_000_000_000)
  priceMinor!: number;
}

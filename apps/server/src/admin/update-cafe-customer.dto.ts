import { IsBoolean, IsOptional, IsString, Length } from 'class-validator';

export class UpdateCafeCustomerDto {
  @IsOptional()
  @IsString()
  @Length(1, 50)
  code?: string;

  @IsOptional()
  @IsString()
  @Length(1, 200)
  name?: string;

  @IsOptional()
  @IsString()
  @Length(1, 200)
  contact?: string;

  @IsOptional()
  @IsString()
  @Length(1, 500)
  notes?: string;

  @IsOptional()
  @IsBoolean()
  active?: boolean;
}

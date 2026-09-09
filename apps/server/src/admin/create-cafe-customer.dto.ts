import { IsOptional, IsString, Length } from 'class-validator';

export class CreateCafeCustomerDto {
  @IsString()
  @Length(1, 50)
  code!: string;

  @IsString()
  @Length(1, 200)
  name!: string;

  @IsOptional()
  @IsString()
  @Length(1, 200)
  contact?: string;

  @IsOptional()
  @IsString()
  @Length(1, 500)
  notes?: string;
}

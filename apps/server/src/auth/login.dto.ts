import { Transform } from 'class-transformer';
import { IsString, Length, MinLength, MaxLength } from 'class-validator';

export class LoginDto {
  @Transform(({ value }: { value: unknown }) => typeof value === 'string' ? value.trim() : value)
  @IsString()
  @Length(1, 100)
  username!: string;

  @IsString()
  @MinLength(8)
  @MaxLength(1024)
  password!: string;
}

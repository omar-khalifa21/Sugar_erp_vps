import { Transform } from 'class-transformer';
import { IsString, Length, MinLength, MaxLength } from 'class-validator';

export class SignupDto {
  @Transform(({ value }: { value: unknown }) => typeof value === 'string' ? value.trim() : value)
  @IsString()
  @Length(1, 100)
  username!: string;

  @Transform(({ value }: { value: unknown }) => typeof value === 'string' ? value.trim() : value)
  @IsString()
  @Length(1, 200)
  displayName!: string;

  @IsString()
  @MinLength(12)
  @MaxLength(1024)
  password!: string;
}

import { Transform } from 'class-transformer';
import { IsOptional, IsString, IsUUID, Length, MinLength, MaxLength } from 'class-validator';

export class CreateUserDto {
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

  @IsUUID()
  roleId!: string;

  @IsOptional()
  @IsUUID()
  siteId?: string;
}

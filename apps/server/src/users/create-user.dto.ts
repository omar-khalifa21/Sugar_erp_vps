import { IsOptional, IsString, IsUUID, Length, MinLength } from 'class-validator';

export class CreateUserDto {
  @IsString()
  @Length(1, 100)
  username!: string;

  @IsString()
  @Length(1, 200)
  displayName!: string;

  @IsString()
  @MinLength(12)
  password!: string;

  @IsUUID()
  roleId!: string;

  @IsOptional()
  @IsUUID()
  siteId?: string;
}

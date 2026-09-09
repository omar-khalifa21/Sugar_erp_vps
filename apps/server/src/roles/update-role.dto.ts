import { ArrayUnique, IsArray, IsBoolean, IsOptional, IsString, Length } from 'class-validator';

export class UpdateRoleDto {
  @IsOptional()
  @IsString()
  @Length(1, 50)
  code?: string;

  @IsOptional()
  @IsString()
  @Length(1, 100)
  name?: string;

  @IsOptional()
  @IsArray()
  @ArrayUnique()
  @IsString({ each: true })
  permissions?: string[];

  @IsOptional()
  @IsBoolean()
  active?: boolean;
}

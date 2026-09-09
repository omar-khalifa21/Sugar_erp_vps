import { ArrayUnique, IsArray, IsString, Length } from 'class-validator';

export class CreateRoleDto {
  @IsString()
  @Length(1, 50)
  code!: string;

  @IsString()
  @Length(1, 100)
  name!: string;

  @IsArray()
  @ArrayUnique()
  @IsString({ each: true })
  permissions!: string[];
}

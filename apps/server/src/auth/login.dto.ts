import { IsString, Length, MinLength } from 'class-validator';

export class LoginDto {
  @IsString()
  @Length(1, 100)
  username!: string;

  @IsString()
  @MinLength(8)
  password!: string;
}

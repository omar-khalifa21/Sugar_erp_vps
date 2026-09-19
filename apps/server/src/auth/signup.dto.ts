import { IsString, Length, MinLength } from 'class-validator';

export class SignupDto {
  @IsString()
  @Length(1, 100)
  username!: string;

  @IsString()
  @Length(1, 200)
  displayName!: string;

  @IsString()
  @MinLength(12)
  password!: string;
}

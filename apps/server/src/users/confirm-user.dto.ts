import { IsString, Length } from 'class-validator';

export class ConfirmUserDto {
  @IsString()
  @Length(1, 100)
  confirmation!: string;
}

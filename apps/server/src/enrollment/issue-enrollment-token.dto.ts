import { IsInt, IsOptional, Max, Min } from 'class-validator';

export class IssueEnrollmentTokenDto {
  @IsOptional()
  @IsInt()
  @Min(5)
  @Max(1440)
  expiresInMinutes?: number;
}

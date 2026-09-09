import { Type } from 'class-transformer';
import { ArrayMinSize, IsArray, IsInt, IsString, IsUUID, Length, Matches, Min, ValidateNested } from 'class-validator';

class ConflictDecisionLineDto {
  @IsUUID()
  lineId!: string;

  @IsString()
  @Matches(/^\d+$/)
  finalScaled!: string;
}

export class DecideConflictDto {
  @IsInt()
  @Min(1)
  expectedVersion!: number;

  @IsString()
  @Length(3, 500)
  reason!: string;

  @IsArray()
  @ArrayMinSize(1)
  @ValidateNested({ each: true })
  @Type(() => ConflictDecisionLineDto)
  lines!: ConflictDecisionLineDto[];
}

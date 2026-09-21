import { IsBase64, IsIn, IsInt, IsString, IsUUID, Matches, Max, MaxLength, Min } from 'class-validator';

export class ReportUploadDto {
  @IsUUID()
  shiftId!: string;

  @IsInt()
  @Min(1)
  @Max(1000)
  reportVersion!: number;

  @Matches(/^\d{4}-\d{2}-\d{2}$/)
  businessDate!: string;

  @IsIn(['MORNING', 'EVENING'])
  shiftKind!: 'MORNING' | 'EVENING';

  @IsString()
  @MaxLength(255)
  filename!: string;

  @Matches(/^[0-9a-f]{64}$/)
  contentHash!: string;

  @IsInt()
  @Min(1)
  @Max(10_000_000)
  byteLength!: number;

  @IsBase64()
  @MaxLength(13_400_000)
  contentBase64!: string;
}

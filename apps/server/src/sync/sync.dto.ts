import { Type } from 'class-transformer';
import {
  ArrayMaxSize,
  Equals,
  IsArray,
  IsInt,
  IsISO8601,
  IsObject,
  IsOptional,
  IsString,
  IsUUID,
  Matches,
  Max,
  Min,
  ValidateNested,
} from 'class-validator';

export class SyncEventDto {
  @IsUUID('4')
  id!: string;

  @IsInt()
  @Min(1)
  device_sequence!: number;

  @IsString()
  @Matches(/^[a-z][a-z0-9_.-]{2,119}$/)
  event_type!: string;

  @IsInt()
  @Min(1)
  schema_version!: number;

  @IsISO8601({ strict: true })
  occurred_at!: string;

  @IsObject()
  payload!: Record<string, unknown>;

  @IsOptional()
  @IsArray()
  @ArrayMaxSize(50)
  @IsUUID('4', { each: true })
  dependencies?: string[];

  @IsString()
  @Matches(/^[0-9a-f]{64}$/)
  content_hash!: string;
}

export class SyncPushDto {
  @Equals('1.0')
  contract_version!: '1.0';

  @IsInt()
  @Min(1)
  stream_epoch!: number;

  @IsArray()
  @ArrayMaxSize(100)
  @ValidateNested({ each: true })
  @Type(() => SyncEventDto)
  events!: SyncEventDto[];
}

export class SyncPullQueryDto {
  @IsOptional()
  @IsString()
  cursor?: string;

  @IsOptional()
  @Type(() => Number)
  @IsInt()
  @Min(1)
  @Max(100)
  limit?: number;
}

export class SyncAckDto {
  @IsString()
  cursor!: string;
}

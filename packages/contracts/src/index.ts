export const CONTRACT_VERSION = '1.0' as const;
export const MAX_SYNC_BATCH_SIZE = 100;
export const MAX_PULL_PAGE_SIZE = 100;

export type DeviceProfile = 'BRANCH_TYPE_1' | 'BRANCH_TYPE_2' | 'KITCHEN';

export interface EventEnvelope<TPayload extends Record<string, unknown> = Record<string, unknown>> {
  id: string;
  device_sequence: number;
  event_type: string;
  schema_version: number;
  occurred_at: string;
  payload: TPayload;
  dependencies?: string[];
  content_hash: string;
}

export interface SyncPushRequest {
  contract_version: typeof CONTRACT_VERSION;
  stream_epoch: number;
  events: EventEnvelope[];
}

export interface SyncPushResult {
  contract_version: typeof CONTRACT_VERSION;
  next_expected_sequence: number;
  results: Array<{
    id: string;
    status: 'accepted' | 'duplicate';
    server_position: string;
  }>;
}

export interface ApiError {
  code: string;
  message: string;
  correlation_id: string;
  retryable: boolean;
  field_errors?: string[];
  current_version?: number;
  expected_sequence?: number;
}

export const contractErrorCodes = [
  'UNAUTHENTICATED',
  'FORBIDDEN',
  'VALIDATION_ERROR',
  'CONTENT_HASH_MISMATCH',
  'IDEMPOTENCY_KEY_REUSE',
  'SEQUENCE_GAP',
  'SEQUENCE_REPLAY',
  'DEPENDENCY_NOT_READY',
  'STREAM_EPOCH_MISMATCH',
  'DEVICE_NOT_BOOTSTRAPPED',
  'ACTIVE_WRITER_EXISTS',
  'STALE_VERSION',
  'BUSINESS_RULE_VIOLATION',
  'RATE_LIMITED',
  'INTERNAL_ERROR',
] as const;

export type ContractErrorCode = (typeof contractErrorCodes)[number];

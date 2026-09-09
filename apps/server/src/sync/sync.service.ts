import { ConfigService } from '@nestjs/config';
import { HttpException, Injectable, UnauthorizedException } from '@nestjs/common';
import { Prisma } from '@prisma/client';
import { createHash, createHmac, timingSafeEqual } from 'node:crypto';
import { PrismaService } from '../prisma/prisma.service';
import { DeviceAuthService } from './device-auth.service';
import { SyncEventDto, SyncPushDto } from './sync.dto';

@Injectable()
export class SyncService {
  private readonly cursorKey: string;

  constructor(
    private readonly prisma: PrismaService,
    private readonly deviceAuth: DeviceAuthService,
    config: ConfigService,
  ) {
    this.cursorKey = config.getOrThrow<string>('JWT_SECRET');
  }

  async push(deviceId: string | undefined, credential: string | undefined, input: SyncPushDto) {
    const device = await this.deviceAuth.authenticate(deviceId, credential);
    if (input.stream_epoch !== device.streamEpoch) {
      throw contractError(409, 'STREAM_EPOCH_MISMATCH', 'Device stream epoch is stale', false, {
        current_version: device.streamEpoch,
      });
    }

    return this.prisma.$transaction(
      async (transaction) => {
        await transaction.$queryRaw(
          Prisma.sql`SELECT "device_id" FROM "device_sync_states" WHERE "device_id" = ${device.id}::uuid FOR UPDATE`,
        );
        const state = await transaction.deviceSyncState.findUnique({ where: { deviceId: device.id } });
        if (!state) throw contractError(409, 'DEVICE_NOT_BOOTSTRAPPED', 'Device sync state is missing', false);
        let expected = state.nextExpectedSequence;
        const accepted: Array<{ id: string; status: 'accepted' | 'duplicate'; server_position: string }> = [];
        const acceptedInBatch = new Set<string>();

        for (const event of input.events) {
          const computedHash = computeEventHash(event);
          if (computedHash !== event.content_hash) {
            throw contractError(422, 'CONTENT_HASH_MISMATCH', 'Event content hash does not match its canonical content', false);
          }
          const existing = await transaction.syncEvent.findUnique({ where: { id: event.id } });
          if (existing) {
            if (existing.contentHash !== event.content_hash || existing.deviceId !== device.id) {
              throw contractError(409, 'IDEMPOTENCY_KEY_REUSE', 'Event id was already used for different content', false);
            }
            accepted.push({ id: event.id, status: 'duplicate', server_position: existing.serverPosition.toString() });
            acceptedInBatch.add(event.id);
            continue;
          }
          if (event.device_sequence !== expected) {
            throw contractError(
              409,
              event.device_sequence > expected ? 'SEQUENCE_GAP' : 'SEQUENCE_REPLAY',
              event.device_sequence > expected
                ? 'An earlier device event has not been received yet'
                : 'Device sequence was already consumed by another event id',
              event.device_sequence > expected,
              { expected_sequence: expected },
            );
          }
          await this.assertDependencies(transaction, event.dependencies ?? [], acceptedInBatch);
          const saved = await transaction.syncEvent.create({
            data: {
              id: event.id,
              deviceId: device.id,
              siteId: device.siteId,
              streamEpoch: input.stream_epoch,
              deviceSequence: event.device_sequence,
              eventType: event.event_type,
              schemaVersion: event.schema_version,
              occurredAt: new Date(event.occurred_at),
              payload: event.payload as Prisma.InputJsonValue,
              dependencies: (event.dependencies ?? []) as Prisma.InputJsonValue,
              contentHash: event.content_hash,
            },
          });
          accepted.push({ id: event.id, status: 'accepted', server_position: saved.serverPosition.toString() });
          acceptedInBatch.add(event.id);
          expected += 1;
        }

        await transaction.deviceSyncState.update({
          where: { deviceId: device.id },
          data: { nextExpectedSequence: expected, ...(accepted.length ? { lastAcceptedAt: new Date() } : {}) },
        });
        return {
          contract_version: '1.0' as const,
          next_expected_sequence: expected,
          results: accepted,
        };
      },
      { isolationLevel: Prisma.TransactionIsolationLevel.Serializable },
    );
  }

  async pull(
    deviceId: string | undefined,
    credential: string | undefined,
    cursor: string | undefined,
    requestedLimit: number | undefined,
  ) {
    const device = await this.deviceAuth.authenticate(deviceId, credential);
    const after = cursor ? this.decodeCursor(cursor, device.id) : 0n;
    const limit = requestedLimit ?? 100;
    const rows = await this.prisma.syncEvent.findMany({
      where: { siteId: device.siteId, serverPosition: { gt: after } },
      orderBy: { serverPosition: 'asc' },
      take: limit + 1,
    });
    const hasMore = rows.length > limit;
    const page = rows.slice(0, limit);
    const position = page.at(-1)?.serverPosition ?? after;
    return {
      contract_version: '1.0' as const,
      compatibility: { minimum: '1.0', current: '1.0' },
      cursor: this.encodeCursor(position, device.id),
      has_more: hasMore,
      events: page.map((event) => ({
        id: event.id,
        origin_device_id: event.deviceId,
        origin_site_id: event.siteId,
        stream_epoch: event.streamEpoch,
        device_sequence: event.deviceSequence,
        event_type: event.eventType,
        schema_version: event.schemaVersion,
        occurred_at: event.occurredAt.toISOString(),
        received_at: event.receivedAt.toISOString(),
        payload: event.payload,
        dependencies: event.dependencies,
        content_hash: event.contentHash,
        server_position: event.serverPosition.toString(),
      })),
    };
  }

  async acknowledge(deviceId: string | undefined, credential: string | undefined, cursor: string) {
    const device = await this.deviceAuth.authenticate(deviceId, credential);
    const position = this.decodeCursor(cursor, device.id);
    await this.prisma.$transaction(async (transaction) => {
      await transaction.$queryRaw(
        Prisma.sql`SELECT "device_id" FROM "device_sync_cursors" WHERE "device_id" = ${device.id}::uuid FOR UPDATE`,
      );
      const current = await transaction.deviceSyncCursor.findUnique({ where: { deviceId: device.id } });
      if (!current) throw contractError(409, 'DEVICE_NOT_BOOTSTRAPPED', 'Device cursor is missing', false);
      if (position > current.lastServerPosition) {
        await transaction.deviceSyncCursor.update({
          where: { deviceId: device.id },
          data: { lastServerPosition: position, acknowledgedAt: new Date() },
        });
      }
    });
    return { acknowledged: true, server_position: position.toString() };
  }

  private async assertDependencies(
    transaction: Prisma.TransactionClient,
    dependencies: string[],
    acceptedInBatch: Set<string>,
  ): Promise<void> {
    const unresolved = dependencies.filter((dependency) => !acceptedInBatch.has(dependency));
    if (!unresolved.length) return;
    const known = await transaction.syncEvent.count({ where: { id: { in: unresolved } } });
    if (known !== unresolved.length) {
      throw contractError(409, 'DEPENDENCY_NOT_READY', 'A referenced event has not arrived yet', true);
    }
  }

  private encodeCursor(position: bigint, deviceId: string): string {
    const payload = Buffer.from(JSON.stringify({ v: 1, d: deviceId, p: position.toString() })).toString('base64url');
    const signature = createHmac('sha256', this.cursorKey).update(payload).digest('base64url');
    return `${payload}.${signature}`;
  }

  private decodeCursor(cursor: string, deviceId: string): bigint {
    try {
      const [payload, signature] = cursor.split('.');
      if (!payload || !signature) throw new Error('Malformed cursor');
      const expected = createHmac('sha256', this.cursorKey).update(payload).digest('base64url');
      const actualBytes = Buffer.from(signature);
      const expectedBytes = Buffer.from(expected);
      if (actualBytes.length !== expectedBytes.length || !timingSafeEqual(actualBytes, expectedBytes)) {
        throw new Error('Invalid signature');
      }
      const decoded = JSON.parse(Buffer.from(payload, 'base64url').toString('utf8')) as {
        v: number;
        d: string;
        p: string;
      };
      if (decoded.v !== 1 || decoded.d !== deviceId || !/^\d+$/.test(decoded.p)) throw new Error('Invalid scope');
      return BigInt(decoded.p);
    } catch {
      throw new UnauthorizedException('Sync cursor is invalid for this device');
    }
  }
}

export function computeEventHash(event: Omit<SyncEventDto, 'content_hash'>): string {
  return createHash('sha256')
    .update(
      canonicalJson({
        id: event.id,
        device_sequence: event.device_sequence,
        event_type: event.event_type,
        schema_version: event.schema_version,
        occurred_at: event.occurred_at,
        payload: event.payload,
        dependencies: event.dependencies ?? [],
      }),
      'utf8',
    )
    .digest('hex');
}

function canonicalJson(value: unknown): string {
  if (value === null || typeof value !== 'object') return JSON.stringify(value);
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(',')}]`;
  return `{${Object.entries(value as Record<string, unknown>)
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([key, entry]) => `${JSON.stringify(key)}:${canonicalJson(entry)}`)
    .join(',')}}`;
}

function contractError(
  status: number,
  code: string,
  message: string,
  retryable: boolean,
  details: Record<string, unknown> = {},
): HttpException {
  return new HttpException({ code, message, retryable, ...details }, status);
}

import { ConfigService } from '@nestjs/config';
import { HttpException, Injectable, UnauthorizedException } from '@nestjs/common';
import { DeviceProfile, Prisma, SiteType } from '@prisma/client';
import { createHash, createHmac, timingSafeEqual } from 'node:crypto';
import { PrismaService } from '../prisma/prisma.service';
import { DeviceAuthService } from './device-auth.service';
import { SyncEventDto, SyncPushDto } from './sync.dto';
import { TransferProjectionService } from './transfer-projection.service';

@Injectable()
export class SyncService {
  private readonly cursorKey: string;

  constructor(
    private readonly prisma: PrismaService,
    private readonly deviceAuth: DeviceAuthService,
    config: ConfigService,
    private readonly transfers: TransferProjectionService,
  ) {
    this.cursorKey = config.getOrThrow<string>('JWT_SECRET');
  }

  async bootstrap(deviceId: string | undefined, credential: string | undefined, appVersion?: string) {
    const device = await this.deviceAuth.authenticate(deviceId, credential, appVersion);
    const [site, catalog, customers, recipes, stock] = await Promise.all([
      this.prisma.site.findUniqueOrThrow({ where: { id: device.siteId }, select: { id: true, name: true, type: true, timezone: true } }),
      this.prisma.item.findMany({
        where: { active: true },
        include: { siteRetailPrices: { where: { siteId: device.siteId }, take: 1 } },
        orderBy: { nameAr: 'asc' },
      }),
      this.prisma.cafeCustomer.findMany({ where: { active: true }, include: { prices: true }, orderBy: { name: 'asc' } }),
      device.profile === 'KITCHEN' ? this.prisma.recipe.findMany({ where: { active: true }, include: { components: true }, orderBy: { productItemId: 'asc' } }) : Promise.resolve([]),
      this.prisma.stockBalance.findMany({ where: { siteId: device.siteId }, select: { itemId: true, location: true, quantityScaled: true, version: true, asOfAt: true } }),
    ]);
    return { contract_version: '1.0', site, catalog: catalog.map(({ siteRetailPrices, ...item }) => ({
      ...item,
      retailPriceMinor: siteRetailPrices[0]?.priceMinor ?? item.retailPriceMinor,
      priceVersion: siteRetailPrices[0]?.version ?? 0,
    })), customers, recipes: recipes.map((recipe) => ({ id: recipe.id, product_item_id: recipe.productItemId,
      output_scaled: recipe.outputScaled.toString(), version: recipe.version, components: recipe.components.map((component) => ({
        ingredient_item_id: component.ingredientItemId, quantity_scaled: component.quantityScaled.toString(),
      })) })), stock: stock.map((row) => ({ ...row, quantityScaled: row.quantityScaled.toString() })), as_of: new Date().toISOString() };
  }

  async push(deviceId: string | undefined, credential: string | undefined, input: SyncPushDto, appVersion?: string) {
    const device = await this.deviceAuth.authenticate(deviceId, credential, appVersion);
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
          if (event.event_type === 'kitchen_request.submitted') {
            if (device.profile === DeviceProfile.KITCHEN) {
              throw contractError(403, 'WRONG_PROFILE', 'Only a branch can submit a kitchen request', false);
            }
            const kitchenCount = await transaction.site.count({ where: { type: SiteType.KITCHEN, active: true } });
            if (kitchenCount !== 1) {
              throw contractError(409, 'KITCHEN_ROUTE_UNAVAILABLE', 'Exactly one active kitchen is required to route this request', true);
            }
          }
          if (event.event_type === 'shipment.dispatched') {
            if (device.profile !== DeviceProfile.KITCHEN) {
              throw contractError(403, 'WRONG_PROFILE', 'Only a kitchen can dispatch a shipment', false);
            }
            const destination = event.payload.destination_site_id;
            if (typeof destination !== 'string' || !/^[0-9a-f-]{36}$/i.test(destination)) {
              throw contractError(422, 'INVALID_DESTINATION', 'Shipment destination is missing or invalid', false);
            }
            const branch = await transaction.site.findUnique({ where: { id: destination } });
            if (!branch?.active || branch.type === SiteType.KITCHEN) {
              throw contractError(422, 'INVALID_DESTINATION', 'Shipment destination must be an active branch', false);
            }
          }
          if (event.event_type === 'kitchen_request.received') {
            if (device.profile !== DeviceProfile.KITCHEN) {
              throw contractError(403, 'WRONG_PROFILE', 'Only a kitchen can acknowledge a branch request', false);
            }
            const destination = event.payload.destination_site_id;
            if (typeof destination !== 'string' || !/^[0-9a-f-]{36}$/i.test(destination)) {
              throw contractError(422, 'INVALID_DESTINATION', 'Request acknowledgement destination is missing or invalid', false);
            }
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
              occurredAtRaw: event.occurred_at,
              payload: event.payload as Prisma.InputJsonValue,
              dependencies: (event.dependencies ?? []) as Prisma.InputJsonValue,
              contentHash: event.content_hash,
            },
          });
          await this.transfers.apply(transaction, event, device.siteId, device.profile);
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
    appVersion?: string,
  ) {
    const device = await this.deviceAuth.authenticate(deviceId, credential, appVersion);
    const after = cursor ? this.decodeCursor(cursor, device.id) : 0n;
    const limit = requestedLimit ?? 100;
    // A device already committed its own outbox locally before upload. Echoing those
    // events back can only duplicate work and can block older clients before they
    // reach changes published by another device or by the server.
    const routes: Prisma.SyncEventWhereInput[] = [{ siteId: device.siteId, deviceId: { not: device.id } }];
    // The catalog identity is global. Price remains site-scoped in the payload,
    // and desktop clients preserve their own price when the event belongs to a
    // different site.
    routes.push({ eventType: { in: ['catalog.item_published', 'catalog.item.updated', 'catalog.item.deleted'] } });
    if (device.profile !== DeviceProfile.BRANCH_TYPE_1)
      routes.push({ eventType: { in: ['cafe_customer.created', 'cafe_customer.updated', 'cafe_customer.archived', 'cafe_customer.price_list_updated'] } });
    if (device.profile === DeviceProfile.KITCHEN) {
      // Requests are owned by branch writers, but must reach the kitchen feed.
      routes.push({
        eventType: 'kitchen_request.submitted',
        site: { type: { in: [SiteType.BRANCH_TYPE_1, SiteType.BRANCH_TYPE_2] } },
      });
      routes.push({
        eventType: { in: ['incoming_receipt.accepted', 'incoming_receipt.disputed'] },
        site: { type: { in: [SiteType.BRANCH_TYPE_1, SiteType.BRANCH_TYPE_2] } },
      });
      routes.push({
        eventType: 'kitchen_return.dispatched',
        site: { type: { in: [SiteType.BRANCH_TYPE_1, SiteType.BRANCH_TYPE_2] } },
      });
    } else {
      // Kitchen dispatches are visible only to the addressed branch. Do not
      // broadcast them to every branch or trust a free-form site id alone.
      routes.push({
        eventType: 'shipment.dispatched',
        site: { type: SiteType.KITCHEN },
        payload: { path: ['destination_site_id'], equals: device.siteId },
      });
      routes.push({
        eventType: 'kitchen_request.received',
        site: { type: SiteType.KITCHEN },
        payload: { path: ['destination_site_id'], equals: device.siteId },
      });
    }
    const rows = await this.prisma.syncEvent.findMany({
      where: { OR: routes, serverPosition: { gt: after } },
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
        occurred_at: event.occurredAtRaw,
        received_at: event.receivedAt.toISOString(),
        payload: event.payload,
        dependencies: event.dependencies,
        content_hash: event.contentHash,
        server_position: event.serverPosition.toString(),
      })),
    };
  }

  async listKitchenRequests() {
    const requests = await this.prisma.kitchenRequest.findMany({
      include: { requestingSite: { select: { id: true, name: true, type: true } }, lines: true, shipments: { select: { id: true, status: true } } },
      orderBy: { submittedAt: 'desc' }, take: 200,
    });
    return requests.map((request) => ({
      event_id: request.sourceEventId,
      source_site: request.requestingSite,
      received_at: request.submittedAt.toISOString(),
      as_of: request.submittedAt.toISOString(),
      request: {
        request_id: request.id, requesting_site_id: request.requestingSiteId,
        status: request.status, version: request.version, business_date: request.businessDate,
        lines: request.lines.map((line) => ({
          line_id: line.id, item_id: line.itemId, requested_scaled: line.requestedScaled.toString(),
          sent_scaled: line.sentScaled.toString(), quantity_scale: line.quantityScale,
          name_snapshot: line.nameSnapshot, unit_snapshot: line.unitSnapshot,
        })),
        shipments: request.shipments,
      },
    }));
  }

  async listShipments() {
    const shipments = await this.prisma.kitchenShipment.findMany({
      include: { destinationSite: { select: { id: true, name: true, type: true } }, lines: true, receipt: { include: { lines: true } } },
      orderBy: { dispatchedAt: 'desc' }, take: 200,
    });
    return shipments.map((shipment) => ({
      id: shipment.id, request_id: shipment.requestId, reference: shipment.reference,
      destination_site: shipment.destinationSite, status: shipment.status,
      dispatched_at: shipment.dispatchedAt.toISOString(),
      lines: shipment.lines.map((line) => ({ id: line.id, item_id: line.itemId, sent_scaled: line.sentScaled.toString() })),
      receipt: shipment.receipt && {
        id: shipment.receipt.id, status: shipment.receipt.status,
        counted_at: shipment.receipt.countedAt.toISOString(),
        lines: shipment.receipt.lines.map((line) => ({ shipment_line_id: line.shipmentLineId, counted_scaled: line.countedScaled.toString() })),
      },
    }));
  }

  async listSiteSales(siteId: string) {
    return this.readSiteEvents(siteId, 'sale.completed');
  }

  async listSiteShiftCloses(siteId: string) {
    return this.readSiteEvents(siteId, 'shift.closed');
  }

  private async readSiteEvents(siteId: string, eventType: string) {
    const events = await this.prisma.syncEvent.findMany({
      where: { siteId, eventType },
      orderBy: { serverPosition: 'desc' },
      take: 200,
      select: { id: true, deviceId: true, occurredAt: true, receivedAt: true, payload: true },
    });
    return events.map((event) => ({
      event_id: event.id,
      site_id: siteId,
      device_id: event.deviceId,
      occurred_at: event.occurredAt.toISOString(),
      received_at: event.receivedAt.toISOString(),
      as_of: event.receivedAt.toISOString(),
      data: event.payload,
    }));
  }

  async acknowledge(deviceId: string | undefined, credential: string | undefined, cursor: string, appVersion?: string) {
    const device = await this.deviceAuth.authenticate(deviceId, credential, appVersion);
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

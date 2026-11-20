import { Injectable, Logger, OnModuleDestroy, OnModuleInit } from '@nestjs/common';
import { Prisma } from '@prisma/client';
import { PrismaService } from '../prisma/prisma.service';
import { TransferProjectionService } from './transfer-projection.service';

@Injectable()
export class PendingManualDeductionsService implements OnModuleInit, OnModuleDestroy {
  private readonly logger = new Logger(PendingManualDeductionsService.name);
  private timer?: NodeJS.Timeout;
  private running?: Promise<void>;

  constructor(private readonly prisma: PrismaService, private readonly transfers: TransferProjectionService) {}

  onModuleInit() {
    this.timer = setInterval(() => {
      this.running ??= this.processDue().catch((error: unknown) => {
        this.logger.warn(`Deferred ingredient deduction retry failed (${error instanceof Error ? error.name : 'database error'})`);
      }).finally(() => { this.running = undefined; });
    }, 5000);
    this.timer.unref();
  }

  async onModuleDestroy() {
    clearInterval(this.timer);
    await this.running;
  }

  async processDue() {
    for (let i = 0; i < 20; i++) {
      const found = await this.prisma.$transaction(async tx => {
        const [row] = await tx.$queryRaw<Array<{ id: string; kitchen: string; occurred: Date; payload: Prisma.JsonValue }>>`
          SELECT q.source_event_id AS id, q.kitchen_site_id AS kitchen, e.occurred_at AS occurred, e.payload
          FROM pending_manual_deductions q JOIN sync_events e ON e.id = q.source_event_id
          WHERE q.retry_at <= now() ORDER BY q.retry_at, q.source_event_id LIMIT 1 FOR UPDATE OF q SKIP LOCKED`;
        if (!row) return false;
        const blocked = await this.transfers.deductManualIngredients(tx, row.kitchen, row.id, row.occurred, row.payload, new Date());
        const where = { sourceEventId: row.id };
        if (blocked) {
          await tx.pendingManualDeduction.update({ where, data: { retryAt: new Date(Date.now() + 60_000) } });
        } else {
          await tx.pendingManualDeduction.delete({ where });
        }
        return true;
      }, { isolationLevel: Prisma.TransactionIsolationLevel.Serializable });
      if (!found) break;
    }
  }
}

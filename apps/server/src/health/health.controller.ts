import { Controller, Get, ServiceUnavailableException } from '@nestjs/common';
import { Prisma } from '@prisma/client';
import { Public } from '../common/public.decorator';
import { PrismaService } from '../prisma/prisma.service';

@Controller()
export class HealthController {
  constructor(private readonly prisma: PrismaService) {}

  @Public()
  @Get('health')
  health(): { status: 'ok'; service: 'sugar-erp-api' } {
    return { status: 'ok', service: 'sugar-erp-api' };
  }

  @Public()
  @Get('ready')
  async ready(): Promise<{ status: 'ready'; database: 'ready'; migrations: 'ready' }> {
    try {
      await this.prisma.$queryRaw(Prisma.sql`SELECT 1`);
      const pendingFailures = await this.prisma.$queryRaw<Array<{ count: bigint }>>(
        Prisma.sql`SELECT COUNT(*) AS count FROM "_prisma_migrations" WHERE finished_at IS NULL OR rolled_back_at IS NOT NULL`,
      );
      if (Number(pendingFailures[0]?.count || 0) > 0) throw new Error('Migration is incomplete');
      return { status: 'ready', database: 'ready', migrations: 'ready' };
    } catch {
      throw new ServiceUnavailableException('Database or migration readiness check failed');
    }
  }
}

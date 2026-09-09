import { HealthController } from './health.controller';
import { PrismaService } from '../prisma/prisma.service';

describe('HealthController', () => {
  it('reports process liveness without requiring the database', () => {
    const controller = new HealthController({} as PrismaService);

    expect(controller.health()).toEqual({
      status: 'ok',
      service: 'sugar-erp-api',
    });
  });
});

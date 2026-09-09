import { MiddlewareConsumer, Module, NestModule } from '@nestjs/common';
import { ConfigModule } from '@nestjs/config';
import { APP_GUARD } from '@nestjs/core';
import { AuthModule } from './auth/auth.module';
import { JwtAuthGuard } from './auth/jwt-auth.guard';
import { CorrelationIdMiddleware } from './common/correlation-id.middleware';
import { DevicesModule } from './devices/devices.module';
import { HealthModule } from './health/health.module';
import { ItemsModule } from './items/items.module';
import { PrismaModule } from './prisma/prisma.module';
import { RolesModule } from './roles/roles.module';
import { SitesModule } from './sites/sites.module';
import { UsersModule } from './users/users.module';

function validateEnvironment(config: Record<string, unknown>): Record<string, unknown> {
  if (!config.DATABASE_URL) throw new Error('DATABASE_URL is required');
  if (typeof config.JWT_SECRET !== 'string' || config.JWT_SECRET.length < 32) {
    throw new Error('JWT_SECRET must contain at least 32 characters');
  }
  return config;
}

@Module({
  imports: [
    ConfigModule.forRoot({ isGlobal: true, validate: validateEnvironment }),
    PrismaModule,
    AuthModule,
    HealthModule,
    SitesModule,
    DevicesModule,
    UsersModule,
    RolesModule,
    ItemsModule,
  ],
  providers: [{ provide: APP_GUARD, useClass: JwtAuthGuard }],
})
export class AppModule implements NestModule {
  configure(consumer: MiddlewareConsumer): void {
    consumer.apply(CorrelationIdMiddleware).forRoutes('*');
  }
}

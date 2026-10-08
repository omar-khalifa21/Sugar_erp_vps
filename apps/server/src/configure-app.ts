import { INestApplication, ValidationPipe } from '@nestjs/common';
import { json, urlencoded, type Express, type Request, type Response, type NextFunction } from 'express';
import { ConfigService } from '@nestjs/config';
import { ApiExceptionFilter } from './common/api-exception.filter';

export function configureApp(app: INestApplication): void {
  app.setGlobalPrefix('api/v1');
  const server = app.getHttpAdapter().getInstance() as Express;
  server.disable('x-powered-by');
  // Production has one loopback-bound API behind Nginx, which overwrites XFF.
  server.set('trust proxy', app.get(ConfigService).get('TRUST_PROXY_HOPS') === '1' ? 1 : false);
  app.use((_request: Request, response: Response, next: NextFunction) => {
    response.setHeader('X-Content-Type-Options', 'nosniff');
    response.setHeader('X-Frame-Options', 'DENY');
    response.setHeader('Referrer-Policy', 'no-referrer');
    response.setHeader('Cache-Control', 'no-store');
    next();
  });
  app.use('/api/v1/auth', json({ limit: '16kb' }));
  app.use(json({ limit: '14mb' }));
  app.use(urlencoded({ extended: true, limit: '1mb' }));
  app.useGlobalPipes(
    new ValidationPipe({ whitelist: true, forbidNonWhitelisted: true, transform: true }),
  );
  app.useGlobalFilters(new ApiExceptionFilter());
}

import { INestApplication, ValidationPipe } from '@nestjs/common';
import { json, urlencoded } from 'express';
import { ApiExceptionFilter } from './common/api-exception.filter';

export function configureApp(app: INestApplication): void {
  app.setGlobalPrefix('api/v1');
  app.use(json({ limit: '14mb' }));
  app.use(urlencoded({ extended: true, limit: '1mb' }));
  app.useGlobalPipes(
    new ValidationPipe({ whitelist: true, forbidNonWhitelisted: true, transform: true }),
  );
  app.useGlobalFilters(new ApiExceptionFilter());
}

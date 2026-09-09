import { Injectable, NestMiddleware } from '@nestjs/common';
import { randomUUID } from 'node:crypto';
import type { NextFunction, Request, Response } from 'express';

@Injectable()
export class CorrelationIdMiddleware implements NestMiddleware {
  use(request: Request, response: Response, next: NextFunction): void {
    const supplied = request.header('x-correlation-id');
    const correlationId = supplied && supplied.length <= 100 ? supplied : randomUUID();
    response.locals.correlationId = correlationId;
    response.setHeader('x-correlation-id', correlationId);
    next();
  }
}

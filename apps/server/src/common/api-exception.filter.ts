import {
  ArgumentsHost,
  Catch,
  ExceptionFilter,
  HttpException,
  HttpStatus,
} from '@nestjs/common';
import type { Request, Response } from 'express';

interface ValidationBody {
  message?: string | string[];
  error?: string;
  code?: string;
  retryable?: boolean;
  current_version?: number;
  expected_sequence?: number;
}

@Catch()
export class ApiExceptionFilter implements ExceptionFilter {
  catch(exception: unknown, host: ArgumentsHost): void {
    const http = host.switchToHttp();
    const request = http.getRequest<Request>();
    const response = http.getResponse<Response>();
    const isHttp = exception instanceof HttpException;
    const status = isHttp ? exception.getStatus() : HttpStatus.INTERNAL_SERVER_ERROR;
    const raw = isHttp ? exception.getResponse() : undefined;
    const body: ValidationBody = typeof raw === 'object' && raw !== null ? raw : {};
    const messages = Array.isArray(body.message) ? body.message : undefined;
    const safeMessage =
      typeof body.message === 'string'
        ? body.message
        : isHttp
          ? body.error || 'Request failed'
          : 'Internal server error';

    response.status(status).json({
      code: body.code || this.codeFor(status),
      message: safeMessage,
      correlation_id: String(response.locals.correlationId),
      retryable: body.retryable ?? status >= 500,
      ...(messages ? { field_errors: messages } : {}),
      ...(body.current_version !== undefined ? { current_version: body.current_version } : {}),
      ...(body.expected_sequence !== undefined
        ? { expected_sequence: body.expected_sequence }
        : {}),
      path: request.originalUrl,
    });
  }

  private codeFor(status: number): string {
    const codes: Record<number, string> = {
      400: 'VALIDATION_ERROR',
      401: 'UNAUTHENTICATED',
      403: 'FORBIDDEN',
      404: 'NOT_FOUND',
      409: 'CONFLICT',
      422: 'BUSINESS_RULE_VIOLATION',
      429: 'RATE_LIMITED',
    };
    return codes[status] || 'INTERNAL_ERROR';
  }
}

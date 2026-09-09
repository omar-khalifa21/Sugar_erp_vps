import { Body, Controller, HttpCode, Param, ParseUUIDPipe, Post, Req } from '@nestjs/common';
import type { Request } from 'express';
import { Public } from '../common/public.decorator';
import { Roles } from '../common/roles.decorator';
import { EnrollDeviceDto } from './enroll-device.dto';
import { EnrollmentService } from './enrollment.service';
import { IssueEnrollmentTokenDto } from './issue-enrollment-token.dto';

interface AdminRequest extends Request {
  user: { id: string; roles: string[] };
}

@Controller()
export class EnrollmentController {
  constructor(private readonly enrollment: EnrollmentService) {}

  @Roles('ADMIN')
  @Post('sites/:siteId/enrollment-tokens')
  issue(
    @Param('siteId', ParseUUIDPipe) siteId: string,
    @Req() request: AdminRequest,
    @Body() input: IssueEnrollmentTokenDto,
  ) {
    return this.enrollment.issue(siteId, request.user.id, input);
  }

  @Public()
  @Post('enrollment')
  @HttpCode(201)
  enroll(@Body() input: EnrollDeviceDto) {
    return this.enrollment.enroll(input);
  }
}

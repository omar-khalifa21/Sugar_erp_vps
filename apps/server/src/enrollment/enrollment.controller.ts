import { BadRequestException, Body, Controller, Get, HttpCode, Param, ParseUUIDPipe, Post, Put, Req, UseGuards } from '@nestjs/common';
import type { Request } from 'express';
import { AuthRateLimitGuard } from '../auth/auth-rate-limit.guard';
import { KitchenPasswordEnrollmentDto, SetKitchenPasswordDto } from './kitchen-password.dto';
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

  @Roles('ADMIN')
  @Get('admin/kitchen-enrollment')
  async kitchenPasswordStatus() {
    return { setting: await this.enrollment.kitchenPasswordStatus() };
  }

  @Roles('ADMIN')
  @Put('admin/kitchen-enrollment')
  setKitchenPassword(@Req() request: AdminRequest, @Body() input: SetKitchenPasswordDto) {
    if (!request.secure) throw new BadRequestException('HTTPS required');
    return this.enrollment.setKitchenPassword(input, request.user.id);
  }

  @Public()
  @UseGuards(AuthRateLimitGuard)
  @Post('enrollment/kitchen')
  enrollKitchen(@Req() request: Request, @Body() input: KitchenPasswordEnrollmentDto) {
    if (!request.secure) throw new BadRequestException('HTTPS required');
    return this.enrollment.enrollKitchen(input);
  }

  @Public()
  @Post('enrollment')
  @HttpCode(201)
  enroll(@Body() input: EnrollDeviceDto) {
    return this.enrollment.enroll(input);
  }
}

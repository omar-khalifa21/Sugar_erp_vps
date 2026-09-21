import { Body, Controller, Get, Headers, Param, ParseUUIDPipe, Post, Res, StreamableFile } from '@nestjs/common';
import type { Response } from 'express';
import { Public } from '../common/public.decorator';
import { Roles } from '../common/roles.decorator';
import { DeviceAuthService } from '../sync/device-auth.service';
import { ReportUploadDto } from './report-upload.dto';
import { ReportsService } from './reports.service';

@Public()
@Controller('reports')
export class DeviceReportsController {
  constructor(private readonly reports: ReportsService, private readonly deviceAuth: DeviceAuthService) {}

  @Post('upload')
  async upload(
    @Headers('x-device-id') deviceId: string | undefined,
    @Headers('x-device-secret') credential: string | undefined,
    @Headers('x-app-version') appVersion: string | undefined,
    @Body() input: ReportUploadDto,
  ) {
    const device = await this.deviceAuth.authenticate(deviceId, credential, appVersion);
    return this.reports.upload(device, input);
  }
}

@Roles('ADMIN')
@Controller('admin/reports')
export class AdminReportsController {
  constructor(private readonly reports: ReportsService) {}

  @Get()
  list() {
    return this.reports.list();
  }

  @Get(':id/download')
  async download(@Param('id', ParseUUIDPipe) id: string, @Res({ passthrough: true }) response: Response) {
    const report = await this.reports.download(id);
    const fallback = 'shift-report.xlsx';
    response.setHeader('Content-Type', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet');
    response.setHeader('Content-Length', report.byteLength.toString());
    response.setHeader('Content-Disposition', `attachment; filename="${fallback}"; filename*=UTF-8''${encodeURIComponent(report.filename)}`);
    response.setHeader('ETag', `"${report.contentHash}"`);
    return new StreamableFile(report.content);
  }
}
